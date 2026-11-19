using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ConstructionSaaS.Infrastructure.Persistence;

// Signals carry no notification data. Readers always re-query their authorized inbox.
public sealed class NotificationSignal : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<Guid, Subscription> subscriptions = new();
    private readonly ConditionalWeakTable<DbTransaction, HashSet<(int Company, string User)>> pending = new();

    public void AfterSave(DbTransaction? transaction, int companyId, string userId)
    {
        if (transaction == null) Publish(companyId, userId);
        else pending.GetOrCreateValue(transaction).Add((companyId, userId));
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (!pending.TryGetValue(transaction, out var recipients)) return;
        pending.Remove(transaction);
        foreach (var recipient in recipients) Publish(recipient.Company, recipient.User);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionCommitted(transaction, eventData);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => pending.Remove(transaction);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        pending.Remove(transaction);
        return Task.CompletedTask;
    }

    public Subscription Subscribe(int companyId, string userId)
    {
        var subscription = new Subscription(this, companyId, userId);
        subscriptions[subscription.Id] = subscription;
        return subscription;
    }

    public void Publish(int companyId, string userId)
    {
        foreach (var subscription in subscriptions.Values)
            if (subscription.CompanyId == companyId && subscription.UserId == userId)
                subscription.Updates.Writer.TryWrite(true);
    }

    public sealed class Subscription(NotificationSignal owner, int companyId, string userId) : IDisposable
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal int CompanyId { get; } = companyId;
        internal string UserId { get; } = userId;
        internal Channel<bool> Updates { get; } = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { await Updates.Reader.ReadAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            owner.subscriptions.TryRemove(Id, out _);
            Updates.Writer.TryComplete();
        }
    }
}
