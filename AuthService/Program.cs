using AuthService.Auth;
using AuthService.Data;
using AuthService.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Em "Testing", a connection string vem do WebApplicationFactory (banco dedicado por execução de teste).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = BearerAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = BearerAuthenticationDefaults.AuthenticationScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, JwtBearerAuthenticationHandler>(
        BearerAuthenticationDefaults.AuthenticationScheme,
        _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(BearerAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        var allowedOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"];
        if (!string.IsNullOrWhiteSpace(allowedOrigins))
        {
            policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Auth Service API",
        Version = "v1",
        Description = "Autenticação, autorização e cadastros correlatos. Rotas versionadas sob o prefixo /api/v1."
    });
    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT no header Authorization (Bearer {token})."
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
    // Marca todas as operações como exigindo Bearer no documento OpenAPI; a rota /api/v1/auth/login
    // continua acessível sem autenticação por estar marcada com [AllowAnonymous] no controller — esse
    // anonimato é controlado em runtime e não precisa ser refletido no contrato exposto pela UI.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
    options.DocumentFilter<V1PathPrefixDocumentFilter>();
    options.OperationFilter<ContractExamplesOperationFilter>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseCors("CorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

// Documentação Swagger é exposta apenas a usuários autenticados (issue #95).
// - O documento OpenAPI sob /swagger/{documentName}/swagger.json é registrado como endpoint
//   roteado e já é coberto pela fallback policy do AuthorizationMiddleware (Bearer obrigatório).
// - A UI servida pelo Swashbuckle em /docs é middleware terminal (não é endpoint), então é
//   protegida pelo SwaggerAuthorizationMiddleware abaixo, que autentica via Bearer antes de
//   delegar ao Swashbuckle.
app.UseMiddleware<SwaggerAuthorizationMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "docs";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth Service API v1");
});

app.MapGroup("/api/v1").MapControllers();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await SystemSeeder.EnsureSystemsAsync(db);
        await SystemTokenTypeSeeder.EnsureSystemTokenTypesAsync(db);
        await AuthenticatorRoutesSeeder.EnsureRoutesAsync(db);
        await PermissionTypeSeeder.EnsurePermissionTypesAsync(db);
        await AuthenticatorPermissionsSeeder.EnsurePermissionsAsync(db);
        await RootUserSeeder.EnsureRootUserAsync(db);
        await RootRolePermissionsSeeder.EnsureRootRolePermissionsAsync(db);
    }
}

await app.RunAsync();
