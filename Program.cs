using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using SistemaAgendamentos;

var builder = WebApplication.CreateBuilder(args);
var tokenService = new TokenService(builder.Configuration);

builder.Services.AddSingleton(tokenService);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new AgendaStore(Path.Combine(
    builder.Environment.ContentRootPath, "App_Data", "agenda.sqlite3")));

builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = tokenService.SigningKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidIssuer = TokenService.Issuer,
            ValidateIssuer = true,
            ValidAudience = TokenService.Audience,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.Services.GetRequiredService<AgendaStore>().Initialize();
app.MapPost("/api/login", (LoginRequest input, TokenService tokens, TimeProvider clock, HttpContext context) =>
{
    var response = tokens.Login(input, clock.GetUtcNow());
    context.Response.Headers["Cache-Control"] = "no-store";
    return response is null ? Results.Unauthorized() : Results.Ok(response);
}).RequireRateLimiting("login");

app.MapAgenda();
app.Run();
