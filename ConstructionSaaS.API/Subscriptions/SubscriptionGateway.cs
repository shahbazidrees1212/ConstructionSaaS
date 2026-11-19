using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.API.Subscriptions;

public record SubscriptionCharge(string Reference,decimal Amount,string Currency,DateTime PaidAtUtc);
public record SubscriptionState(string Status,string? SubscriptionId,string? CustomerId,DateTime? PaidThroughUtc,bool CancelAtPeriodEnd,List<SubscriptionCharge> Payments);
public record SubscriptionCheckout(string Id,string Url);
public interface ISubscriptionGateway
{
    Task ValidatePlan(CompanySubscription s);
    Task<SubscriptionCheckout> Create(CompanySubscription s,string email);
    Task<SubscriptionState> Read(CompanySubscription s);
    Task Cancel(CompanySubscription s);
    Task<string> Manage(CompanySubscription s);
    Task<bool> Verify(string provider,string body,IHeaderDictionary headers);
}
public class SubscriptionGateway(IHttpClientFactory factory,SubscriptionCatalog catalog,ILogger<SubscriptionGateway> logger) : ISubscriptionGateway
{
    private async Task<HttpClient> Client(string provider)
    {
        if(!catalog.Configured(provider)) throw new PaymentUnavailableException("The SaaS owner's subscription payment account is not configured.");
        var c=factory.CreateClient("payments");
        c.BaseAddress=new Uri(provider=="Stripe" ? "https://api.stripe.com/v1/" : catalog.Sandbox ? "https://api-m.sandbox.paypal.com/" : "https://api-m.paypal.com/");
        if(provider=="Stripe") {
            c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",catalog.Setting(provider,"SecretKey"));
            c.DefaultRequestHeaders.Add("Stripe-Version","2026-10-28.endive");
        } else {
            c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(catalog.Setting(provider,"ClientId")+":"+catalog.Setting(provider,"ClientSecret"))));
            try {
                using var r=await c.PostAsync("v1/oauth2/token",Form(new(){["grant_type"]="client_credentials"}));
                var token=await Body(r);
                c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",S(token,"access_token"));
            } catch {c.Dispose();throw;}
        }
        return c;
    }
    private async Task<JsonElement> Body(HttpResponseMessage response)
    {
        if(!response.IsSuccessStatusCode) {
            logger.LogWarning("Subscription provider request failed: HTTP {Status}",(int)response.StatusCode);
            throw new PaymentUnavailableException("The subscription provider could not complete this request. Check the existing subscription before trying again; contact the SaaS owner if this continues.");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static FormUrlEncodedContent Form(Dictionary<string,string> values)=>new(values);
    private static string S(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()!:"";
    private static DateTime? Epoch(JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number?DateTimeOffset.FromUnixTimeSeconds(v.GetInt64()).UtcDateTime:null;
    private static DateTime? Date(JsonElement e,string name)=>DateTimeOffset.TryParse(S(e,name),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var d)?d.UtcDateTime:null;
    private static string Id(JsonElement e)=>e.ValueKind==JsonValueKind.String?e.GetString()!:S(e,"id");
    private static void Require(bool ok) {if(!ok)throw new PaymentUnavailableException("Subscription verification failed. The plan, amount, company or payment environment does not match.");}
    private void Mode(CompanySubscription s)=>Require(s.IsSandbox==catalog.Sandbox);
    public async Task ValidatePlan(CompanySubscription s)
    {
        Mode(s);
        using var c=await Client(s.Provider);
        using var r=await c.GetAsync((s.Provider=="Stripe"?"prices/":"v1/billing/plans/")+Uri.EscapeDataString(s.ProviderPlanId));
        var p=await Body(r);
        if(s.Provider=="Stripe") {
            Require(p.GetProperty("active").GetBoolean() && p.GetProperty("livemode").GetBoolean()!=s.IsSandbox && S(p,"id")==s.ProviderPlanId);
            var recurring=p.GetProperty("recurring");
            Require(p.GetProperty("unit_amount").GetDecimal()==s.Amount*100 && S(p,"currency").ToUpperInvariant()==s.Currency && S(recurring,"interval")==s.Interval && recurring.GetProperty("interval_count").GetInt32()==1 && S(recurring,"usage_type")=="licensed");
        } else {
            Require(S(p,"status")=="ACTIVE" && S(p,"id")==s.ProviderPlanId);
            var cycles=p.GetProperty("billing_cycles"); Require(cycles.GetArrayLength()==1);
            var cycle=cycles[0]; var money=cycle.GetProperty("pricing_scheme").GetProperty("fixed_price"); var frequency=cycle.GetProperty("frequency");
            Require(S(cycle,"tenure_type")=="REGULAR" && cycle.GetProperty("total_cycles").GetInt32()==0 && frequency.GetProperty("interval_count").GetInt32()==1 && S(frequency,"interval_unit").ToLowerInvariant()==s.Interval && S(money,"currency_code")==s.Currency && decimal.Parse(S(money,"value"),CultureInfo.InvariantCulture)==s.Amount);
            if(p.TryGetProperty("payment_preferences",out var preferences)&&preferences.TryGetProperty("setup_fee",out var fee)) Require(decimal.Parse(S(fee,"value"),CultureInfo.InvariantCulture)==0);
        }
    }
    public async Task<SubscriptionCheckout> Create(CompanySubscription s,string email)
    {
        Mode(s);
        using var c=await Client(s.Provider);
        using var request=new HttpRequestMessage(HttpMethod.Post,s.Provider=="Stripe"?"checkout/sessions":"v1/billing/subscriptions");
        request.Headers.Add(s.Provider=="Stripe"?"Idempotency-Key":"PayPal-Request-Id",(s.Provider=="Stripe"?"subscription-":"")+s.AttemptId.ToString("N"));
        if(s.Provider=="Stripe") request.Content=Form(new(){
            ["mode"]="subscription",["line_items[0][price]"]=s.ProviderPlanId,["line_items[0][quantity]"]="1",
            ["customer_email"]=email,["client_reference_id"]=s.AttemptId.ToString("N"),
            ["success_url"]=catalog.ReturnUrl,["cancel_url"]=catalog.ReturnUrl,
            ["metadata[attempt_id]"]=s.AttemptId.ToString("N"),["subscription_data[metadata][attempt_id]"]=s.AttemptId.ToString("N"),
            ["subscription_data[metadata][company_id]"]=s.CompanyId.ToString(CultureInfo.InvariantCulture)
        });
        else request.Content=JsonContent.Create(new{plan_id=s.ProviderPlanId,custom_id=s.AttemptId.ToString("N"),application_context=new{return_url=catalog.ReturnUrl,cancel_url=catalog.ReturnUrl,user_action="SUBSCRIBE_NOW",shipping_preference="NO_SHIPPING"}});
        using var response=await c.SendAsync(request); var body=await Body(response);
        var url=S(body,"url");
        if(s.Provider=="PayPal") foreach(var l in body.GetProperty("links").EnumerateArray()) if(S(l,"rel")=="approve") url=S(l,"href");
        Require(PaymentGateway.IsCheckoutUrl(s.Provider,url));
        return new(S(body,"id"),url);
    }
    public async Task<SubscriptionState> Read(CompanySubscription s)
    {
        Mode(s); using var c=await Client(s.Provider);
        if(s.Provider=="Stripe") {
            var subscriptionId=s.ProviderSubscriptionId;
            if(subscriptionId==null) {
                Require(s.CheckoutId!=null);
                using var cr=await c.GetAsync("checkout/sessions/"+Uri.EscapeDataString(s.CheckoutId!));var session=await Body(cr);
                Require(S(session,"client_reference_id")==s.AttemptId.ToString("N") && session.GetProperty("livemode").GetBoolean()!=s.IsSandbox && S(session,"mode")=="subscription");
                if(!session.TryGetProperty("subscription",out var sub)||sub.ValueKind==JsonValueKind.Null)
                    return new(S(session,"status")=="expired"?"Expired":"Pending",null,null,null,false,[]);
                subscriptionId=Id(sub);
            }
            using var response=await c.GetAsync("subscriptions/"+Uri.EscapeDataString(subscriptionId)+"?expand[]=latest_invoice");var body=await Body(response);
            Require(S(body,"id")==subscriptionId && body.GetProperty("livemode").GetBoolean()!=s.IsSandbox && S(body.GetProperty("metadata"),"attempt_id")==s.AttemptId.ToString("N") && S(body.GetProperty("metadata"),"company_id")==s.CompanyId.ToString(CultureInfo.InvariantCulture));
            var items=body.GetProperty("items").GetProperty("data");Require(items.GetArrayLength()==1);
            var item=items[0];var price=item.GetProperty("price");
            Require(S(price,"id")==s.ProviderPlanId && item.GetProperty("quantity").GetInt32()==1 && price.GetProperty("unit_amount").GetDecimal()==s.Amount*100 && S(price,"currency").ToUpperInvariant()==s.Currency);
            var state=S(body,"status") switch{"active"=>"Active","canceled"=>"Cancelled","incomplete_expired"=>"Expired","past_due"=>"PastDue","unpaid" or "paused"=>"Suspended",_=>"Pending"};
            DateTime? paidThrough=null;List<SubscriptionCharge> payments=[];
            if(body.TryGetProperty("latest_invoice",out var invoice)&&invoice.ValueKind==JsonValueKind.Object && S(invoice,"status")=="paid") {
                Require(S(invoice,"currency").ToUpperInvariant()==s.Currency && invoice.GetProperty("amount_paid").GetDecimal()==s.Amount*100);
                paidThrough=Epoch(item,"current_period_end")??Epoch(body,"current_period_end");
                var paidAt=invoice.TryGetProperty("status_transitions",out var transitions)?Epoch(transitions,"paid_at"):null;
                if(paidAt!=null)payments.Add(new(S(invoice,"id"),s.Amount,s.Currency,paidAt.Value));
            }
            return new(state,subscriptionId,Id(body.GetProperty("customer")),paidThrough,body.GetProperty("cancel_at_period_end").GetBoolean(),payments);
        } else {
            var id=s.ProviderSubscriptionId??s.CheckoutId;Require(id!=null);
            using var response=await c.GetAsync("v1/billing/subscriptions/"+Uri.EscapeDataString(id!)); var body=await Body(response);
            Require(S(body,"id")==id && S(body,"custom_id")==s.AttemptId.ToString("N") && S(body,"plan_id")==s.ProviderPlanId && (!body.TryGetProperty("plan_overridden",out var overridden)||!overridden.GetBoolean()));
            var state=S(body,"status") switch{"ACTIVE"=>"Active","CANCELLED"=>"Cancelled","EXPIRED"=>"Expired","SUSPENDED"=>"Suspended",_=>"Pending"};
            DateTime? paidThrough=null;List<SubscriptionCharge> payments=[];
            if(body.TryGetProperty("billing_info",out var info) && info.TryGetProperty("last_payment",out var last)) {
                var amount=last.GetProperty("amount");
                Require(S(amount,"currency_code")==s.Currency && decimal.Parse(S(amount,"value"),CultureInfo.InvariantCulture)==s.Amount);
                var at=Date(last,"time");
                if(at!=null) {
                    // Only advance entitlement after the last payment is reported by PayPal itself.
                    paidThrough=Date(info,"next_billing_time");
                    if(paidThrough==null && state=="Cancelled") paidThrough=s.PaidThroughUtc;
                    payments.Add(new(id+":"+at.Value.ToString("yyyyMMddHHmmss"),s.Amount,s.Currency,at.Value));
                }
                if(info.TryGetProperty("outstanding_balance",out var debt)&&decimal.Parse(S(debt,"value"),CultureInfo.InvariantCulture)>0) state="PastDue";
            }
            return new(state,id,null,paidThrough,state=="Cancelled",payments);
        }
    }
    public async Task Cancel(CompanySubscription s)
    {
        Mode(s);if(s.ProviderSubscriptionId==null)throw new PaymentUnavailableException("Refresh the subscription before cancelling.");
        using var c=await Client(s.Provider);
        using var request=new HttpRequestMessage(HttpMethod.Post,s.Provider=="Stripe"?"subscriptions/"+Uri.EscapeDataString(s.ProviderSubscriptionId):"v1/billing/subscriptions/"+Uri.EscapeDataString(s.ProviderSubscriptionId)+"/cancel");
        request.Headers.Add(s.Provider=="Stripe"?"Idempotency-Key":"PayPal-Request-Id","c-"+s.AttemptId.ToString("N"));
        request.Content=s.Provider=="Stripe"?Form(new(){["cancel_at_period_end"]="true"}):JsonContent.Create(new{reason="Company administrator cancelled SaaS subscription renewal."});
        using var response=await c.SendAsync(request);
        if(response.StatusCode!=System.Net.HttpStatusCode.NoContent) _=await Body(response);
    }
    public async Task<bool> Verify(string provider,string body,IHeaderDictionary headers)
    {
        if(!catalog.Configured(provider))return false;
        if(provider=="Stripe")return PaymentGateway.VerifyStripeSignature(body,headers["Stripe-Signature"].ToString(),catalog.Setting(provider,"WebhookSecret")!,DateTimeOffset.UtcNow);
        foreach(var name in new[]{"PAYPAL-AUTH-ALGO","PAYPAL-CERT-URL","PAYPAL-TRANSMISSION-ID","PAYPAL-TRANSMISSION-SIG","PAYPAL-TRANSMISSION-TIME"}) if(string.IsNullOrWhiteSpace(headers[name]))return false;
        using var c=await Client(provider);
        using var response=await c.PostAsJsonAsync("v1/notifications/verify-webhook-signature",new{
            auth_algo=headers["PAYPAL-AUTH-ALGO"].ToString(),cert_url=headers["PAYPAL-CERT-URL"].ToString(),transmission_id=headers["PAYPAL-TRANSMISSION-ID"].ToString(),
            transmission_sig=headers["PAYPAL-TRANSMISSION-SIG"].ToString(),transmission_time=headers["PAYPAL-TRANSMISSION-TIME"].ToString(),webhook_id=catalog.Setting(provider,"WebhookId"),webhook_event=JsonSerializer.Deserialize<JsonElement>(body)});
        return S(await Body(response),"verification_status")=="SUCCESS";
    }
    public async Task<string> Manage(CompanySubscription s)
    {
        Mode(s);
        if(s.Provider!="Stripe"||string.IsNullOrEmpty(s.CustomerId))throw new PaymentUnavailableException("A verified Stripe subscription is required to manage payment details.");
        using var c=await Client("Stripe");
        using var response=await c.PostAsync("billing_portal/sessions",Form(new(){["customer"]=s.CustomerId,["return_url"]=catalog.ReturnUrl}));
        var url=S(await Body(response),"url");
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="billing.stripe.com"||!uri.IsDefaultPort||uri.UserInfo!="")throw new PaymentUnavailableException("Invalid billing portal address.");
        return url;
    }
}
