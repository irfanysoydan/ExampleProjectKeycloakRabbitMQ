using Accounting.Api.Configuration;
using Accounting.Api.Controllers;
using Accounting.Api.Publishing;
using Accounting.Api.Status;
using Accounting.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

var keycloak = builder.Configuration.GetRequiredSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
    ?? throw new InvalidOperationException("Keycloak configuration is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Inside Docker the API reaches Keycloak by service name, but tokens are issued for localhost.
        options.Authority = keycloak.Authority;
        options.TokenValidationParameters.ValidIssuer = keycloak.PublicIssuer;
        options.Audience = keycloak.Audience;
        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(InvoicesController.SubmitPolicy, policy => policy.RequireClaim("roles", "invoice-submitter"));

builder.Services.AddOptions<RabbitMqOptions>()
    .BindConfiguration(RabbitMqOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<InvoiceStatusStore>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/demo/config", () => new { issuer = keycloak.PublicIssuer, clientId = keycloak.ClientId });

app.Run();
