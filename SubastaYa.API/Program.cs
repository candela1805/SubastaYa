using SubastaYa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using SubastaYa.Application.Abstractions;
using SubastaYa.Application.Features.Users;
using System.Text.Json.Serialization;
using SubastaYa.Domain.Models;
using SubastaYa.API.Services;
using SubastaYa.API.Hubs;
using SubastaYa.API.Workers;
using SubastaYa.API.Authentication;
using SubastaYa.API.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using SubastaYa.API.Adapters;

using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Infrastructure.Repositories;
using SubastaYa.Application.Features.Bids.Commands.PlaceBid;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Application.Features.Bids.Queries.GetBidHistory;
using SubastaYa.Application.Features.Bids.Queries.GetRoomState;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Abstractions.Auditing;
using SubastaYa.Infrastructure.Auditing;
using SubastaYa.Application.Features.Wallet.Commands.DepositWallet;
using SubastaYa.Application.Features.Wallet.DTOs;
using SubastaYa.Application.Features.Wallet.Queries;
using SubastaYa.Application.Features.Auctions.Commands;
using SubastaYa.Application.Features.Auctions.DTOs;
using SubastaYa.Application.Features.Auctions.Queries;
using SubastaYa.Application.Abstractions.Security;
using SubastaYa.Application.Features.Auth;
using SubastaYa.Infrastructure.Security;

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
builder.Services.AddScoped<IPasswordService, IdentityPasswordService>();

builder.Services.AddScoped<
    ICommandHandler<RegisterUserCommand, AuthResult>,
    RegisterUserHandler>();

builder.Services.AddScoped<
    ICommandHandler<LoginUserCommand, AuthResult?>,
    LoginUserHandler>();
builder.Services.AddScoped<
    IQueryHandler<GetUserProfileQuery, UserProfileResult?>,
    GetUserProfileHandler>();

builder.Services.AddScoped<
    ICommandHandler<UpdateUserProfileCommand, UserProfileResult?>,
    UpdateUserProfileHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<TimeProvider>(
    TimeProvider.System);

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

builder.Services.AddDbContextFactory<ApplicationDbContext>(
    options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Scoped);

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

builder.Services.AddScoped<IAuctionRepository, AuctionRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuctionNotifier, SignalRAuctionNotifier>();
builder.Services.AddScoped<IQueryHandler<SearchAuctionsQuery, PagedAuctionResult>, SearchAuctionsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetAuctionCategoriesQuery, IReadOnlyList<string>>, GetAuctionCategoriesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetSellerAuctionsQuery, IReadOnlyList<AuctionResult>>, GetSellerAuctionsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetUserBidActivitiesQuery, IReadOnlyList<BidActivityResult>>, GetUserBidActivitiesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetAuctionCycleCandidatesQuery, AuctionCycleCandidatesResult>, GetAuctionCycleCandidatesQueryHandler>();
builder.Services.AddScoped<ICommandHandler<CreateAuctionCommand, AuctionResult>, CreateAuctionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateAuctionCommand, AuctionResult>, UpdateAuctionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteAuctionCommand, bool>, DeleteAuctionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ActivateAuctionCommand, EstadoSubasta?>, ActivateAuctionCommandHandler>();
builder.Services.AddScoped<IAuctionClosingRepository, AuctionClosingRepository>();
builder.Services.AddScoped<ICommandHandler<CloseAuctionCommand, SubastaYa.Application.Features.Auctions.Commands.AuctionClosingResult>, CloseAuctionCommandHandler>();

builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<
    IQueryHandler<GetWalletBalanceQuery, WalletBalanceResult?>,
    GetWalletBalanceQueryHandler>();
builder.Services.AddScoped<
    ICommandHandler<DepositWalletCommand, WalletBalanceResult?>,
    DepositWalletCommandHandler>();
builder.Services.AddScoped<
    IQueryHandler<GetWalletTransactionsQuery, IReadOnlyList<WalletTransactionResult>?>,
    GetWalletTransactionsQueryHandler>();
builder.Services.AddScoped<
    IQueryHandler<GetRetainedFundsQuery, RetainedFundsResult?>,
    GetRetainedFundsQueryHandler>();

builder.Services.AddScoped<IBidAuditWriter, BidAuditWriter>();

// CQRS - Command: realizar una puja
builder.Services.AddScoped<
    ICommandHandler<PlaceBidCommand, PlaceBidResult>,
    PlaceBidCommandHandler>();

// Notificaciones de pujas mediante SignalR
builder.Services.AddScoped<IBidNotifier, SignalRBidNotifier>();

// CQRS - Query: consultar historial de pujas
builder.Services.AddScoped<IBidReadRepository, BidReadRepository>();
builder.Services.AddScoped<IPlaceBidRepository, PlaceBidRepository>();


builder.Services.AddScoped<
    IQueryHandler<GetBidHistoryQuery, IReadOnlyList<BidHistoryResult>>,
    GetBidHistoryQueryHandler>();

// QUERY: Consultar estado de la sala
builder.Services.AddScoped<
    IQueryHandler<GetRoomStateQuery, BidRoomStateResult>,
    GetRoomStateQueryHandler>();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services
    .AddOptions<DevelopmentSeedOptions>()
    .Bind(builder.Configuration.GetSection(
        DevelopmentSeedOptions.SectionName));
builder.Services.AddScoped<DevelopmentDataSeeder>();
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
                "DevelopmentAuthentication:UserId debe ser un GUID vÃƒÆ’Ã‚Â¡lido.");
        }

        if (usuarioDemoId != DevelopmentDataSeeder.DemoUserId)
        {
            throw new InvalidOperationException(
                "DevelopmentAuthentication:UserId no coincide con el usuario demo esperado.");
        }

        var seeder = scope.ServiceProvider
            .GetRequiredService<DevelopmentDataSeeder>();
        await seeder.SeedAsync(
            developmentAuthentication.Name,
            developmentAuthentication.Email);
    }

    app.UseCors("Frontend");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<AuctionHub>("/hubs/auctions");

app.Run();

public partial class Program;
