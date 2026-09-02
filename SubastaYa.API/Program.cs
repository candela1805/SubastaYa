using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Data;
using System.Text.Json.Serialization;
using SubastaYa.API.Models;
using SubastaYa.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IAuctionService, AuctionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();

    var dbContext =
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var usuarioDemoId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    var usuarioDemo = await dbContext.Usuarios
        .Include(usuario => usuario.Billetera)
        .SingleOrDefaultAsync(usuario => usuario.Id == usuarioDemoId);

    if (usuarioDemo is null)
    {
        usuarioDemo = new Usuario
        {
            Id = usuarioDemoId,
            Nombre = "Usuario Demo",
            Email = "demo@subastaya.com",
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

    app.UseCors("Frontend");
}

app.MapControllers();

app.Run();
