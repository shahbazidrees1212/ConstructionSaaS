using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.API.Subscriptions;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

static class SubscriptionChecks
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Subscriptions:Enabled"]="true",["Subscriptions:Mode"]="Sandbox",["Subscriptions:WebBaseUrl"]="https://example.test",
            ["Subscriptions:Stripe:SecretKey"]="sk_test_platform",["Subscriptions:Stripe:WebhookSecret"]="whsec_fixture",
            ["Subscriptions:PayPal:ClientId"]="platform-client",["Subscriptions:PayPal:ClientSecret"]="platform-secret",["Subscriptions:PayPal:WebhookId"]="WH-fixture",
            ["Subscriptions:Plans:monthly:Name"]="Workspace",["Subscriptions:Plans:monthly:Amount"]="25",["Subscriptions:Plans:monthly:Currency"]="USD",["Subscriptions:Plans:monthly:StripePriceId"]="price_monthly",["Subscriptions:Plans:monthly:PayPalPlanId"]="P-MONTHLY"
        }).Build();
        var env=new TestEnvironment();var catalog=new SubscriptionCatalog(config,env);var now=DateTime.UtcNow;
        CompanySubscription Record()=>new(){CompanyId=1,PlanKey="monthly",PlanName="Workspace",Amount=25,Currency="USD",Provider="Stripe",ProviderPlanId="price_monthly",IsSandbox=true,Interval="month"};
        var entitlement=Record();
        check(!catalog.HasAccess(null,now),"Unsubscribed company is denied when subscriptions are enabled");
        entitlement.Status="Active";check(!catalog.HasAccess(entitlement,now),"Active provider flag alone cannot grant unpaid access");
        entitlement.PaidThroughUtc=now.AddDays(30);check(catalog.HasAccess(entitlement,now),"Verified paid active company has access");
        entitlement.Status="Cancelled";check(catalog.HasAccess(entitlement,now),"Cancelled renewal preserves paid-through access");
        check(!SubscriptionCatalog.CanReplace(entitlement,now),"Cannot create second subscription while paid access remains");
        entitlement.PaidThroughUtc=now.AddSeconds(-1);check(!catalog.HasAccess(entitlement,now)&&SubscriptionCatalog.CanReplace(entitlement,now),"Expired cancelled company loses access and can resubscribe");
        entitlement.PaidThroughUtc=now.AddDays(1);entitlement.Status="PastDue";check(!catalog.HasAccess(entitlement,now),"Past-due payment cannot unlock workspace");
        entitlement.Status="Active";env.EnvironmentName="Production";check(!catalog.HasAccess(entitlement,now),"Sandbox subscription never grants production access");env.EnvironmentName="Testing";
        config["Subscriptions:Enabled"]="false";check(catalog.HasAccess(null,now),"Explicit rollout switch preserves existing workspace access");config["Subscriptions:Enabled"]="true";
        check(catalog.PublicPlans.Count==1&&catalog.PublicPlans[0].Amount==25&&catalog.Configured("Stripe"),"Plan catalog uses owner configuration");

        var record=Record();bool cancelled=false;bool wrongAmount=false;bool wrongOwner=false;bool wrongPrice=false;bool paid=true;
        var requests=new List<(string Path,string Body,string? Key)>();
        var gateway=new SubscriptionGateway(new Factory(new Handler(async request=> {
            var path=request.RequestUri!.AbsolutePath;var form=request.Content==null?"":await request.Content.ReadAsStringAsync();
            requests.Add((path,form,request.Headers.TryGetValues("Idempotency-Key",out var key)?key.Single():request.Headers.TryGetValues("PayPal-Request-Id",out key)?key.Single():null));
            object body;
            if(path=="/v1/prices/price_monthly")body=new{id="price_monthly",active=true,livemode=false,unit_amount=wrongAmount?2600:2500,currency="usd",recurring=new{interval="month",interval_count=1,usage_type="licensed"}};
            else if(path=="/v1/checkout/sessions"&&request.Method==HttpMethod.Post)body=new{id="cs_subscription",url="https://checkout.stripe.com/c/pay/test"};
            else if(path=="/v1/checkout/sessions/cs_subscription")body=new{mode="subscription",livemode=false,client_reference_id=record.AttemptId.ToString("N"),subscription="sub_fixture",status="complete"};
            else if(path=="/v1/billing_portal/sessions")body=new{url="https://billing.stripe.com/p/session/test"};
            else if(path=="/v1/subscriptions/sub_fixture") {
                if(request.Method==HttpMethod.Post)cancelled=true;
                body=new{id="sub_fixture",livemode=false,customer="cus_fixture",status="active",cancel_at_period_end=cancelled,metadata=new{attempt_id=record.AttemptId.ToString("N"),company_id=wrongOwner?"2":"1"},items=new{data=new[]{new{quantity=1,current_period_end=new DateTimeOffset(now.AddDays(30)).ToUnixTimeSeconds(),price=new{id=wrongPrice?"price_other":"price_monthly",unit_amount=2500,currency="usd"}}}},latest_invoice=new{id="in_fixture",status=paid?"paid":"open",currency="usd",amount_paid=2500,status_transitions=new{paid_at=new DateTimeOffset(now).ToUnixTimeSeconds()}}};
            } else if(path=="/v1/oauth2/token")body=new{access_token="fixture"};
            else if(path=="/v1/billing/plans/P-MONTHLY")body=new{id="P-MONTHLY",status="ACTIVE",billing_cycles=new[]{new{tenure_type="REGULAR",total_cycles=0,frequency=new{interval_unit="MONTH",interval_count=1},pricing_scheme=new{fixed_price=new{currency_code="USD",value="25.00"}}}},payment_preferences=new{setup_fee=new{value="0.00"}}};
            else if(path=="/v1/billing/subscriptions"&&request.Method==HttpMethod.Post)body=new{id="I-SUB",links=new[]{new{rel="approve",href="https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=fixture"}}};
            else if(path=="/v1/billing/subscriptions/I-SUB/cancel"){cancelled=true;return new HttpResponseMessage(HttpStatusCode.NoContent);}
            else if(path=="/v1/billing/subscriptions/I-SUB")body=new{id="I-SUB",plan_id="P-MONTHLY",custom_id=record.AttemptId.ToString("N"),plan_overridden=false,status=cancelled?"CANCELLED":"ACTIVE",billing_info=new{last_payment=new{time=now.ToString("O"),amount=new{currency_code="USD",value="25.00"}},next_billing_time=now.AddDays(30).ToString("O"),outstanding_balance=new{value="0.00"}}};
            else throw new Exception("Unexpected provider request: "+path);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(body)};
        })),catalog,NullLogger<SubscriptionGateway>.Instance);
        await gateway.ValidatePlan(record);check(true,"Stripe recurring price checked against server plan");
        wrongAmount=true;await Reject(()=>gateway.ValidatePlan(record),"Mismatched provider price blocks checkout",check);wrongAmount=false;
        var checkout=await gateway.Create(record,"owner@example.test");record.CheckoutId=checkout.Id;
        check(requests.Last().Body.Contains("mode=subscription")&&requests.Last().Body.Contains("price_monthly")&&!requests.Last().Body.Contains("payment_method_types"),"Stripe creates subscription checkout with trusted recurring price");
        var firstKey=requests.Last().Key;await gateway.Create(record,"owner@example.test");check(firstKey==requests.Last().Key,"Subscription retries reuse idempotency key");
        var snapshot=await gateway.Read(record);check(snapshot.Status=="Active"&&snapshot.PaidThroughUtc>now&&snapshot.Payments.Single().Amount==25,"Stripe paid invoice grants verified billing period");
        paid=false;check((await gateway.Read(record)).PaidThroughUtc==null,"Unpaid latest invoice cannot extend access");paid=true;
        wrongOwner=true;await Reject(async()=>{await gateway.Read(record);},"Subscription belonging to another company rejected",check);wrongOwner=false;
        wrongPrice=true;await Reject(async()=>{await gateway.Read(record);},"Different recurring price rejected",check);wrongPrice=false;
        record.ProviderSubscriptionId="sub_fixture";await gateway.Cancel(record);check((await gateway.Read(record)).CancelAtPeriodEnd,"Stripe cancellation stops renewal at period end");
        check(requests.Any(r=>r.Body.Contains("cancel_at_period_end=true")),"Stripe cancellation does not immediately delete subscription");
        record.CustomerId="cus_fixture";
        check((await gateway.Manage(record)).StartsWith("https://billing.stripe.com/")&&requests.Last().Body.Contains("customer=cus_fixture"),"Stripe customer portal uses stored company customer ID");
        check(!await gateway.Verify("Stripe","{}",new HeaderDictionary{{"Stripe-Signature","invalid"}}),"Forged Stripe subscription webhook rejected");
        record.Provider="PayPal";record.ProviderPlanId="P-MONTHLY";record.ProviderSubscriptionId=null;record.CheckoutId=null;cancelled=false;
        await gateway.ValidatePlan(record);checkout=await gateway.Create(record,"owner@example.test");record.CheckoutId=checkout.Id;
        check(requests.Last().Body.Contains("P-MONTHLY")&&requests.Last().Body.Contains(record.AttemptId.ToString("N")),"PayPal uses platform plan and company-bound attempt");
        snapshot=await gateway.Read(record);check(snapshot.Status=="Active"&&snapshot.PaidThroughUtc>now,"PayPal provider-verified payment grants next billing period");
        record.ProviderSubscriptionId="I-SUB";await gateway.Cancel(record);check((await gateway.Read(record)).Status=="Cancelled","PayPal subscription cancellation verified");

        var dbName="ConstructionSaaS_SubscriptionChecks_"+Guid.NewGuid().ToString("N");
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True").Options);
        try {
            await db.Database.MigrateAsync();
            var company=new Company{Name="Subscription tenant",Email="test@example.test",Country="US"};var other=new Company{Name="Other tenant",Email="other@example.test",Country="US"};db.Companies.AddRange(company,other);await db.SaveChangesAsync();
            var fake=new FakeGateway();var service=new SubscriptionService(db,catalog,fake);
            await service.Checkout(company.Id,new(){PlanKey="monthly",Provider="Stripe"});
            check(fake.Created==1,"First subscription checkout created once");
            await service.Checkout(company.Id,new(){PlanKey="monthly",Provider="Stripe"});check(fake.Created==1,"Repeated pending checkout reuses stored checkout");
            await Reject(async()=>{await service.Checkout(company.Id,new(){PlanKey="monthly",Provider="PayPal"});},"Second provider cannot create parallel subscription",check);
            fake.Paid=true;await service.Refresh(company.Id);await service.Refresh(company.Id);
            check(await db.SubscriptionPayments.CountAsync()==1,"Repeated refresh records each subscription payment once");
            check((await service.Overview(company.Id,true)).HasAccess,"Company gains workspace access after verified payment");
            check(!(await service.Overview(other.Id,true)).HasAccess&&(await service.Overview(other.Id,true)).Payments.Count==0,"Company isolation covers subscription and payment history");
            check((await service.Overview(company.Id,false)).Payments.Count==0,"Non-admin cannot read payment history");
            var firstPeriod=(await service.Overview(company.Id,true)).PaidThroughUtc;
            fake.Cycle=1;await service.Refresh(company.Id);
            check(await db.SubscriptionPayments.CountAsync()==2 && (await service.Overview(company.Id,true)).PaidThroughUtc>firstPeriod,"Verified recurring renewal extends access and records a new payment");
            fake.OverrideStatus="PastDue";await service.Refresh(company.Id);
            check(!(await service.Overview(company.Id,true)).HasAccess,"Failed renewal blocks workspace despite an earlier active state");
            fake.OverrideStatus=null;await service.Refresh(company.Id);
            await Reject(async()=>{await service.Checkout(company.Id,new(){PlanKey="monthly",Provider="Stripe"});},"Active subscription cannot be purchased again",check);
            var ctx=new DefaultHttpContext();ctx.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("company_id",other.Id.ToString()),new Claim(ClaimTypes.Role,"CompanyAdmin")},"test"));ctx.Request.Path="/api/projects";ctx.Response.Body=new MemoryStream();bool called=false;
            var gate=new SubscriptionAccessMiddleware(_=>{called=true;return Task.CompletedTask;});await gate.InvokeAsync(ctx,catalog,db);check(ctx.Response.StatusCode==402&&!called,"API rejects unsubscribed workspace access");
            ctx.Request.Path="/api/subscriptions";called=false;await gate.InvokeAsync(ctx,catalog,db);check(called,"Unsubscribed company can access subscription recovery endpoints");
            await service.Cancel(company.Id);check((await service.Overview(company.Id,true)).CancelAtPeriodEnd,"Cancellation verified through service");
            var raceCompany=new Company{Name="Concurrent tenant",Email="race@example.test",Country="US"};db.Companies.Add(raceCompany);await db.SaveChangesAsync();
            var arrived=0;var raceGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var raceGateway=new FakeGateway { Validate = async()=>{if(Interlocked.Increment(ref arrived)==2)raceGate.SetResult();await raceGate.Task;} };
            async Task<bool> Start() {
                await using var separate=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options);
                try{await new SubscriptionService(separate,catalog,raceGateway).Checkout(raceCompany.Id,new(){PlanKey="monthly",Provider="Stripe"});return true;}
                catch(DbUpdateException){return false;}
            }
            var outcomes=await Task.WhenAll(Start(),Start());
            check(outcomes.Count(x=>x)==1&&raceGateway.Created==1,"Concurrent first subscriptions reserve only one provider checkout");
        } finally { if(!db.Database.GetDbConnection().Database.StartsWith("ConstructionSaaS_SubscriptionChecks_"))throw new Exception("Unsafe database cleanup");await db.Database.EnsureDeletedAsync(); }
    }
    private static async Task Reject(Func<Task> action,string name,Action<bool,string> check){try{await action();check(false,name);}catch(PaymentUnavailableException){check(true,name);}}
    private sealed class TestEnvironment:IHostEnvironment {public string EnvironmentName{get;set;}="Testing";public string ApplicationName{get;set;}="Checks";public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
    private sealed class Factory(HttpMessageHandler handler):IHttpClientFactory{public HttpClient CreateClient(string name)=>new(handler,false);}
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>callback(request);}
    private sealed class FakeGateway:ISubscriptionGateway {
        public int Created;public bool Paid;public bool Cancelled;public int Cycle;public string? OverrideStatus;public Func<Task>? Validate;
        public Task ValidatePlan(CompanySubscription s)=>Validate?.Invoke()??Task.CompletedTask;
        public Task<SubscriptionCheckout> Create(CompanySubscription s,string email){Interlocked.Increment(ref Created);return Task.FromResult(new SubscriptionCheckout("cs_fixture","https://checkout.stripe.com/c/pay/test"));}
        public Task<SubscriptionState> Read(CompanySubscription s)=>Task.FromResult(new SubscriptionState(OverrideStatus??(Paid?"Active":"Pending"),Paid?"sub_fixture":null,"cus_fixture",Paid?DateTime.UtcNow.AddDays(30+30*Cycle):null,Cancelled,Paid?[new("in_paid_"+Cycle,25,"USD",DateTime.UtcNow)]:[]));
        public Task Cancel(CompanySubscription s){Cancelled=true;return Task.CompletedTask;}
        public Task<string> Manage(CompanySubscription s)=>Task.FromResult("https://billing.stripe.com/p/session/test");
        public Task<bool> Verify(string provider,string body,IHeaderDictionary headers)=>Task.FromResult(false);
    }
}
