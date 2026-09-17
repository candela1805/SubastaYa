using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Data;
using System.Text.Json.Serialization;
using SubastaYa.API.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Hubs;
using SubastaYa.API.Workers;
using SubastaYa.API.Authentication;
using SubastaYa.API.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using SubastaYa.API.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new DecimalStringJsonConverter());
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication(
        DevelopmentAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<
        DevelopmentAuthenticationOptions,
        DevelopmentAuthenticationHandler>(
        DevelopmentAuthenticationDefaults.AuthenticationScheme,
        options => builder.Configuration
            .GetSection(
                DevelopmentAuthenticationDefaults.ConfigurationSection)
            .Bind(options));

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<
    IPasswordHasher<Usuario>,
    PasswordHasher<Usuario>>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5500",
                "http://127.0.0.1:5500")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IAuctionService, AuctionService>();
builder.Services.AddScoped<IBidService, BidService>();
builder.Services.AddScoped<IAuctionClosingService, AuctionClosingService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddOptions<AuctionClosingWorkerOptions>()
    .Bind(builder.Configuration.GetSection(
        AuctionClosingWorkerOptions.SectionName))
    .Validate(
        options => options.IntervalSeconds > 0,
        "AuctionClosingWorker:IntervalSeconds debe ser mayor a cero.")
    .Validate(
        options => options.BatchSize is > 0 and <= 500,
        "AuctionClosingWorker:BatchSize debe estar entre 1 y 500.")
    .ValidateOnStart();
builder.Services.AddSingleton<
    IAuctionStateCycleProcessor,
    AuctionStateCycleProcessor>();
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(
            new DecimalStringJsonConverter());
    });
builder.Services.AddHostedService<AuctionStateWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();

    var dbContext =
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var developmentAuthentication = scope.ServiceProvider
        .GetRequiredService<
            IOptionsMonitor<DevelopmentAuthenticationOptions>>()
        .Get(DevelopmentAuthenticationDefaults.AuthenticationScheme);

    if (developmentAuthentication.Enabled)
    {
        if (!Guid.TryParse(
                developmentAuthentication.UserId,
                out var usuarioDemoId))
        {
            throw new InvalidOperationException(
                "DevelopmentAuthentication:UserId debe ser un GUID válido.");
        }

        var usuarioDemo = await dbContext.Usuarios
            .Include(usuario => usuario.Billetera)
            .SingleOrDefaultAsync(usuario => usuario.Id == usuarioDemoId);

        if (usuarioDemo is null)
        {
            usuarioDemo = new Usuario
            {
                Id = usuarioDemoId,
                Nombre = developmentAuthentication.Name,
                Email = developmentAuthentication.Email,
                PasswordHash = "DEMO_NO_USAR_EN_PRODUCCION",
                FechaRegistro = DateTimeOffset.UtcNow
            };

            usuarioDemo.Billetera = new Billetera
            {
                UsuarioId = usuarioDemoId,
                Usuario = usuarioDemo,
                SaldoTotal = 0,
                SaldoRetenido = 0,
                SaldoDisponible = 0
            };

            dbContext.Usuarios.Add(usuarioDemo);
        }
        else if (usuarioDemo.Billetera is null)
        {
            dbContext.Billeteras.Add(new Billetera
            {
                UsuarioId = usuarioDemo.Id,
                Usuario = usuarioDemo,
                SaldoTotal = 0,
                SaldoRetenido = 0,
                SaldoDisponible = 0
            });
        }

        await dbContext.SaveChangesAsync();
    }

    app.UseCors("Frontend");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<AuctionHub>("/hubs/auctions");

app.Run();

public partial class Program;
