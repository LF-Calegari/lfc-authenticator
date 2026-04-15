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
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
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
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT no header Authorization (Bearer {token})."
    });
    options.DocumentFilter<V1PathPrefixDocumentFilter>();
    options.OperationFilter<ContractExamplesOperationFilter>();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

// Swagger antes de autenticação/autorização: documentação e OpenAPI JSON são anônimos.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "docs";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth Service API v1");
});

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/api/v1").MapControllers();

if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await OfficialCatalogSeeder.EnsureCatalogAsync(db);
        await KurttoAccessSeeder.EnsureKurttoAccessAsync(db);
        await DefaultSystemUserSeeder.EnsureDefaultUserAsync(db);
        await LegacyUsersClientSeeder.EnsureLegacyUsersHaveClientAsync(db);
    }
}

await app.RunAsync();
