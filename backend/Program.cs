using Bima.Api.Application;
using Bima.Api.Data;
using Bima.Api.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var jwtSettings = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSettings["SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey must be configured.");
var issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer must be configured.");
var audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("Jwt:Audience must be configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = issuer,
            ValidateAudience = true, ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<FirstPartyAuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IClaimService, DatabaseClaimService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ClaimAttachmentService>();
builder.Services.AddScoped<PasswordRecoveryService>();
builder.Services.AddScoped<OrganizationService>();
var connectionString = builder.Configuration.GetConnectionString("InsuranceDatabase");
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<AccessContext>();
var healthChecks = builder.Services.AddHealthChecks();
builder.Services.AddScoped<IEmailSender>(services =>
    builder.Environment.IsDevelopment()
        ? new DevelopmentEmailSender(services.GetRequiredService<ILogger<DevelopmentEmailSender>>())
        : new UnconfiguredEmailSender());
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<BimaDbContext>(options => options.UseNpgsql(connectionString));
    healthChecks.AddCheck<DatabaseHealthCheck>("postgres");
    builder.Services.AddScoped<IPolicyService, DatabasePolicyService>();
    builder.Services.AddScoped<ICustomerService, DatabaseCustomerService>();
    builder.Services.AddScoped<FirstPartyAuthService>();
    builder.Services.AddScoped<AuditService>();
    builder.Services.AddScoped<UserService>();
}
else
{
    builder.Services.AddSingleton<IPolicyService, SamplePolicyService>();
    builder.Services.AddSingleton<ICustomerService, SampleCustomerService>();
    builder.Services.AddSingleton<IClaimService, SampleClaimService>();
}
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173")
            .WithHeaders("Content-Type", "Authorization")
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS"));
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many authentication requests. Try again later." }, cancellationToken);
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();
    var accessContext = context.RequestServices.GetRequiredService<AccessContext>();
    tenantContext.TenantId = context.User.FindFirstValue("tenant") ?? string.Empty;
    accessContext.UserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    accessContext.Role = context.User.FindFirstValue(ClaimTypes.Role)?.ToLowerInvariant() ?? string.Empty;
    if (Guid.TryParse(accessContext.UserId, out var userId) && !string.IsNullOrWhiteSpace(connectionString))
    {
        var db = context.RequestServices.GetRequiredService<BimaDbContext>();
        var grants = await db.UserPermissions.Where(permission => permission.TenantId == tenantContext.TenantId && permission.UserId == userId).Select(permission => permission.Permission).ToListAsync();
        foreach (var grant in grants.Where(value => Enum.TryParse<Permission>(value, true, out _)))
            accessContext.GrantedPermissions.Add(Enum.Parse<Permission>(grant, true));
    }
    await next();
});

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "bima-api",
    timestamp = DateTimeOffset.UtcNow
}));
app.MapHealthChecks("/health/ready");

app.MapPost("/api/auth/register", async (RegisterRequest request, FirstPartyAuthService authService, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await authService.RegisterAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (LoginRequest request, FirstPartyAuthService authService, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await authService.LoginAsync(request, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/api/users", async (CreateUserRequest request, FirstPartyAuthService authService, AccessContext accessContext, TenantContext tenantContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireAdministrator(accessContext);
        return Results.Created("/api/users", await authService.CreateUserAsync(request, tenantContext.TenantId, accessContext.UserId, cancellationToken));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapPost("/api/auth/refresh", async (RefreshRequest request, FirstPartyAuthService authService, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await authService.RefreshAsync(request, cancellationToken));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/api/auth/logout", async (RefreshRequest request, FirstPartyAuthService authService, CancellationToken cancellationToken) =>
{
    await authService.RevokeAsync(request, cancellationToken);
    return Results.NoContent();
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapGet("/api/auth/sessions", async (FirstPartyAuthService authService, AccessContext accessContext, TenantContext tenantContext, CancellationToken cancellationToken) =>
    Results.Ok(await authService.GetSessionsAsync(accessContext.UserId, tenantContext.TenantId, cancellationToken)))
    .RequireAuthorization();

app.MapDelete("/api/auth/sessions/{sessionId:guid}", async (Guid sessionId, FirstPartyAuthService authService, AccessContext accessContext, TenantContext tenantContext, CancellationToken cancellationToken) =>
{
    try
    {
        await authService.RevokeSessionAsync(sessionId, accessContext.UserId, tenantContext.TenantId, cancellationToken);
        return Results.NoContent();
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
}).RequireAuthorization();

app.MapPost("/api/auth/password-reset/request", async (PasswordResetRequest request, PasswordRecoveryService recoveryService, IHostEnvironment environment, CancellationToken cancellationToken) =>
{
    var token = await recoveryService.RequestResetAsync(request, cancellationToken);
    var response = new Dictionary<string, object?> { ["message"] = "If the account exists, password reset instructions have been issued." };
    if (environment.IsDevelopment() && token is not null)
        response["developmentToken"] = token;
    return Results.Ok(response);
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/api/auth/password-reset/confirm", async (PasswordResetConfirmation request, PasswordRecoveryService recoveryService, CancellationToken cancellationToken) =>
{
    try
    {
        await recoveryService.ResetPasswordAsync(request, cancellationToken);
        return Results.NoContent();
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
}).AllowAnonymous().RequireRateLimiting("auth");

app.MapGet("/api/users", async (UserService userService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireAdministrator(accessContext);
        return Results.Ok(await userService.GetUsersAsync(cancellationToken));
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).RequireAuthorization();

app.MapGet("/api/audit", async (AuditService auditService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireAdministrator(accessContext);
        return Results.Ok(await auditService.GetAsync(cancellationToken));
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).RequireAuthorization();

app.MapGet("/api/organization", async (OrganizationService organizationService, CancellationToken cancellationToken) =>
    Results.Ok(await organizationService.GetAsync(cancellationToken)))
    .RequireAuthorization();

app.MapPatch("/api/organization", async (UpdateOrganizationRequest request, OrganizationService organizationService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireAdministrator(accessContext); return Results.Ok(await organizationService.UpdateAsync(request, accessContext.UserId, cancellationToken)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapGet("/api/users/{userId:guid}/permissions", async (Guid userId, OrganizationService organizationService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireAdministrator(accessContext); return Results.Ok(await organizationService.GetPermissionsAsync(userId, cancellationToken)); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapPut("/api/users/{userId:guid}/permissions/{permission}", async (Guid userId, string permission, OrganizationService organizationService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireAdministrator(accessContext); await organizationService.GrantPermissionAsync(userId, permission, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapDelete("/api/users/{userId:guid}/permissions/{permission}", async (Guid userId, string permission, OrganizationService organizationService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireAdministrator(accessContext); await organizationService.RevokePermissionAsync(userId, permission, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapPatch("/api/users/{userId:guid}/role", async (Guid userId, ChangeRoleRequest request, UserService userService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireAdministrator(accessContext);
        return Results.Ok(await userService.ChangeRoleAsync(userId, request.Role, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).RequireAuthorization();

app.MapGet("/api/policies", async (IPolicyService policyService, CancellationToken cancellationToken) =>
    Results.Ok(await policyService.GetPoliciesAsync(cancellationToken)))
    .WithName("GetPolicies")
    .RequireAuthorization();

app.MapGet("/api/policies/{number}", async (string number, IPolicyService policyService, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await policyService.GetPolicyAsync(number, cancellationToken)); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
}).RequireAuthorization();

app.MapPost("/api/policies", async (CreatePolicyRequest request, IPolicyService policyService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        var policy = await policyService.CreatePolicyAsync(request, accessContext.UserId, cancellationToken);
        return Results.Created($"/api/policies/{policy.Number}", policy);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).WithName("CreatePolicy")
    .RequireAuthorization();

app.MapPatch("/api/policies/{number}", async (string number, UpdatePolicyRequest request, IPolicyService policyService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        return Results.Ok(await policyService.UpdatePolicyAsync(number, request, accessContext.UserId, cancellationToken));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapDelete("/api/policies/{number}", async (string number, IPolicyService policyService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireWrite(accessContext); await policyService.DeletePolicyAsync(number, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapGet("/api/customers", async (ICustomerService customerService, CancellationToken cancellationToken) =>
    Results.Ok(await customerService.GetCustomersAsync(cancellationToken)))
    .WithName("GetCustomers")
    .RequireAuthorization();

app.MapGet("/api/claims", async (IClaimService claimService, CancellationToken cancellationToken) =>
    Results.Ok(await claimService.GetClaimsAsync(cancellationToken)))
    .WithName("GetClaims")
    .RequireAuthorization();

app.MapGet("/api/claims/{claimNumber}", async (string claimNumber, IClaimService claimService, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await claimService.GetClaimAsync(claimNumber, cancellationToken));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
}).WithName("GetClaim")
    .RequireAuthorization();

app.MapPost("/api/claims", async (CreateClaimRequest request, IClaimService claimService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        var claim = await claimService.CreateClaimAsync(request, accessContext.UserId, cancellationToken);
        return Results.Created($"/api/claims/{claim.ClaimNumber}", claim);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).WithName("CreateClaim")
    .RequireAuthorization();

app.MapGet("/api/billing/invoices", async (BillingService billingService, CancellationToken cancellationToken) =>
    Results.Ok(await billingService.GetInvoicesAsync(cancellationToken)))
    .WithName("GetInvoices")
    .RequireAuthorization();

app.MapPost("/api/billing/invoices", async (CreateInvoiceRequest request, BillingService billingService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        var invoice = await billingService.CreateInvoiceAsync(request, accessContext.UserId, cancellationToken);
        return Results.Created($"/api/billing/invoices/{invoice.InvoiceNumber}", invoice);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).WithName("CreateInvoice")
    .RequireAuthorization();

app.MapGet("/api/billing/invoices/{invoiceNumber}/payments", async (string invoiceNumber, PaymentService paymentService, CancellationToken cancellationToken) =>
    Results.Ok(await paymentService.GetPaymentsAsync(invoiceNumber, cancellationToken)))
    .RequireAuthorization();

app.MapPost("/api/billing/invoices/{invoiceNumber}/payments", async (string invoiceNumber, RecordPaymentRequest request, PaymentService paymentService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        return Results.Ok(await paymentService.RecordPaymentAsync(invoiceNumber, request, accessContext.UserId, cancellationToken));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapPatch("/api/claims/{claimNumber}", async (string claimNumber, UpdateClaimRequest request, IClaimService claimService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireClaimsManager(accessContext);
        return Results.Ok(await claimService.UpdateClaimAsync(claimNumber, request, accessContext.UserId, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).WithName("UpdateClaim")
    .RequireAuthorization();

app.MapDelete("/api/claims/{claimNumber}", async (string claimNumber, IClaimService claimService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireClaimsManager(accessContext); await claimService.DeleteClaimAsync(claimNumber, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapPost("/api/claims/{claimNumber}/approve", async (string claimNumber, IClaimService claimService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireClaimsManager(accessContext);
        var current = await claimService.GetClaimAsync(claimNumber, cancellationToken);
        if (current.Status != "Under Review")
            return Results.Conflict(new { error = "Only claims under review can be approved." });
        return Results.Ok(await claimService.UpdateClaimAsync(claimNumber, new UpdateClaimRequest("Approved", null, null), accessContext.UserId, cancellationToken));
    }
    catch (KeyNotFoundException exception)
    {
        return Results.NotFound(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
}).WithName("ApproveClaim")
    .RequireAuthorization();

app.MapGet("/api/claims/{claimNumber}/attachments", async (string claimNumber, ClaimAttachmentService attachmentService, CancellationToken cancellationToken) =>
    Results.Ok(await attachmentService.GetAsync(claimNumber, cancellationToken)))
    .RequireAuthorization();

app.MapPost("/api/claims/{claimNumber}/attachments", async (string claimNumber, IFormFile file, ClaimAttachmentService attachmentService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        return Results.Created($"/api/claims/{claimNumber}/attachments", await attachmentService.AddAsync(claimNumber, file, accessContext.UserId, cancellationToken));
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapGet("/api/claims/{claimNumber}/attachments/{attachmentId:guid}", async (string claimNumber, Guid attachmentId, ClaimAttachmentService attachmentService, CancellationToken cancellationToken) =>
{
    try
    {
        var attachment = await attachmentService.OpenAsync(claimNumber, attachmentId, cancellationToken);
        return Results.File(attachment.Content, attachment.ContentType, attachment.FileName);
    }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (FileNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
}).RequireAuthorization();

app.MapDelete("/api/claims/{claimNumber}/attachments/{attachmentId:guid}", async (string claimNumber, Guid attachmentId, ClaimAttachmentService attachmentService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireWrite(accessContext); await attachmentService.DeleteAsync(claimNumber, attachmentId, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapPost("/api/customers", async (CreateCustomerRequest request, ICustomerService customerService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try
    {
        AccessControl.RequireWrite(accessContext);
        var customer = await customerService.CreateCustomerAsync(request, accessContext.UserId, cancellationToken);
        return Results.Created($"/api/customers/{customer.Id}", customer);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (UnauthorizedAccessException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
}).WithName("CreateCustomer")
    .RequireAuthorization();

app.MapDelete("/api/customers/{customerId:guid}", async (Guid customerId, ICustomerService customerService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireWrite(accessContext); await customerService.DeleteCustomerAsync(customerId, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.MapDelete("/api/billing/invoices/{invoiceNumber}", async (string invoiceNumber, BillingService billingService, AccessContext accessContext, CancellationToken cancellationToken) =>
{
    try { AccessControl.RequireWrite(accessContext); await billingService.DeleteInvoiceAsync(invoiceNumber, accessContext.UserId, cancellationToken); return Results.NoContent(); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
    catch (UnauthorizedAccessException exception) { return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status403Forbidden); }
}).RequireAuthorization();

app.Run();

