using Microsoft.EntityFrameworkCore;
using SubastaYa.API.Tests.TestInfrastructure;
using SubastaYa.Application.Abstractions.Notifications;
using SubastaYa.Application.Abstractions.Persistence;
using SubastaYa.Application.Exceptions;
using SubastaYa.Application.Features.Bids.Commands.PlaceBid;
using SubastaYa.Application.Features.Bids.DTOs;
using SubastaYa.Domain.Models;
using SubastaYa.Infrastructure.Auditing;
using Xunit;

namespace SubastaYa.API.Tests.CQRS;

public class PlaceBidCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenAuctionDoesNotExist_ThrowsNotFoundAndRollsBack()
    {
        // Arrange
        var repository = new FakePlaceBidRepository();
        var notifier = new FakeBidNotifier();
        var auditWriter = new FakeBidAuditWriter();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            auditWriter);

        var command = new PlaceBidCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1500m);

        // Act
        var exception = await Assert.ThrowsAsync<BidNotFoundException>(
            () => handler.HandleAsync(command));

        // Assert
        Assert.Equal("AUCTION_NOT_FOUND", exception.Code);

        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.RollbackTransactionCount);
        Assert.Equal(0, repository.CommitTransactionCount);
        Assert.Equal(0, notifier.NotificationCount);

        // Auditoría del rechazo
        var audit = Assert.Single(auditWriter.Events);

        Assert.Equal("BidRejected", audit.TipoEvento);
        Assert.Null(audit.SubastaId);
        Assert.Contains(command.SubastaId.ToString(), audit.Detalle);
        Assert.Equal(command.UsuarioId, audit.UsuarioId);
        Assert.Contains("AUCTION_NOT_FOUND", audit.Detalle);
    }

    private sealed class FakePlaceBidRepository : IPlaceBidRepository
    {
        public int BeginTransactionCount { get; private set; }
        public int CommitTransactionCount { get; private set; }
        public int RollbackTransactionCount { get; private set; }
        public List<string> Events { get; } = new();
        public bool SimulateRollbackFailure { get; set; }
        public bool SimulateCommitFailure { get; set; }

        public Task<Subasta?> GetAuctionAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Auction?.Id == subastaId ? Auction : null);
        }

        public Task<IReadOnlyList<Puja>> GetWinningBidsAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Puja> result =
                WinningBid is not null &&
                WinningBid.SubastaId == subastaId &&
                WinningBid.EsGanadora
                    ? new[] { WinningBid }
                    : Array.Empty<Puja>();

            return Task.FromResult(result);
        }

        public Task<bool> HasAnyBidAsync(
        Guid subastaId,
        CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                WinningBid is not null &&
                WinningBid.SubastaId == subastaId);
        }


        public Task<Billetera?> GetWalletAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default)
        {
            if (Wallet?.UsuarioId == usuarioId)
                return Task.FromResult<Billetera?>(Wallet);

            if (PreviousWinnerWallet?.UsuarioId == usuarioId)
                return Task.FromResult<Billetera?>(PreviousWinnerWallet);

            return Task.FromResult<Billetera?>(null);
        }


        public Task BeginTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            BeginTransactionCount++;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;

            if (SimulateConcurrencyConflict)
            {
                throw new BidPersistenceConcurrencyException(
                    "La información de la subasta cambió durante la operación.");
            }

            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
        {
            CommitTransactionCount++;

            if (SimulateCommitFailure)
            {
                Events.Add("CommitFailed");

                throw new InvalidOperationException(
                    "Error simulado durante el Commit.");
            }

            Events.Add("Commit");

            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            RollbackTransactionCount++;

            if (SimulateRollbackFailure)
            {
                throw new InvalidOperationException(
                    "Error simulado durante el rollback.");
            }

            return Task.CompletedTask;
        }

        public void AddBid(Puja puja)
        {
            AddedBids.Add(puja);
        }

        public void AddLedgerTransaction(TransaccionLedger movimiento)
        {
            AddedLedgerTransactions.Add(movimiento);
        }

        public void AddAuditLog(AuditoriaLog auditoria)
        {
            AddedAuditLogs.Add(auditoria);
        }

        public Subasta? Auction { get; set; }
        public Billetera? Wallet { get; set; }

        public List<Puja> AddedBids { get; } = new();
        public List<TransaccionLedger> AddedLedgerTransactions { get; } = new();
        public List<AuditoriaLog> AddedAuditLogs { get; } = new();

        public Puja? WinningBid { get; set; }

        public Billetera? PreviousWinnerWallet { get; set; }

        public int SaveChangesCount { get; private set; }

        public bool SimulateConcurrencyConflict { get; set; }
    }

    private sealed class FakeBidNotifier : IBidNotifier
    {
        public int NotificationCount { get; private set; }

        public Guid? LastAuctionId { get; private set; }

        public PlaceBidResult? LastResult { get; private set; }

        private readonly List<string>? _events;

        public FakeBidNotifier(List<string>? events = null)
        {
            _events = events;
        }

        public Task NotifyBidPlacedAsync(
            Guid subastaId,
            PlaceBidResult result,
            CancellationToken cancellationToken = default)
        {
            NotificationCount++;
            LastAuctionId = subastaId;
            LastResult = result;
            _events?.Add("Notify");

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HandleAsync_WhenBidIsValid_CommitsAndRegistersMovements()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();
        var subastaId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var subasta = new Subasta
        {
            Id = subastaId,
            VendedorId = Guid.NewGuid(),
            Estado = EstadoSubasta.Activa,
            PrecioActual = 1000m,
            IncrementoMinimo = 100m,
            FechaInicioUtc = ahora.AddHours(-1),
            FechaFinUtc = ahora.AddHours(1)
        };

        var billetera = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            SaldoDisponible = 5000m,
            SaldoRetenido = 0m
        };

        var repository = new FakePlaceBidRepository
        {
            Auction = subasta,
            Wallet = billetera
        };

        var notifier = new FakeBidNotifier(repository.Events);

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            new FakeBidAuditWriter());

        var command = new PlaceBidCommand(
            subastaId,
            usuarioId,
            1500m);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.Equal(1500m, result.Monto);
        Assert.Equal("Liderando", result.Estado);
        Assert.Equal(1500m, subasta.PrecioActual);

        Assert.Equal(3500m, billetera.SaldoDisponible);
        Assert.Equal(1500m, billetera.SaldoRetenido);

        Assert.Single(repository.AddedBids);
        Assert.Single(repository.AddedLedgerTransactions);
        Assert.Single(repository.AddedAuditLogs);

        Assert.Equal("BidPlaced", repository.AddedAuditLogs[0].TipoEvento);

        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.CommitTransactionCount);
        Assert.Equal(0, repository.RollbackTransactionCount);

        Assert.Equal(1, notifier.NotificationCount);
        Assert.Equal(subastaId, notifier.LastAuctionId);
        Assert.Same(result, notifier.LastResult);
        Assert.Equal(
        new[] { "Commit", "Notify" },
        repository.Events);
    }

    [Fact]
    public async Task HandleAsync_WhenNewBidOutbidsPreviousWinner_ReleasesPreviousFunds()
    {
        // Arrange
        var subastaId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        var usuarioAnteriorId = Guid.NewGuid();
        var nuevoUsuarioId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var subasta = new Subasta
        {
            Id = subastaId,
            VendedorId = vendedorId,
            Estado = EstadoSubasta.Activa,
            PrecioActual = 1500m,
            IncrementoMinimo = 100m,
            FechaInicioUtc = ahora.AddHours(-1),
            FechaFinUtc = ahora.AddHours(1)
        };

        var pujaAnterior = new Puja
        {
            Id = Guid.NewGuid(),
            SubastaId = subastaId,
            UsuarioId = usuarioAnteriorId,
            Monto = 1500m,
            EsGanadora = true,
            FechaUtc = ahora.AddMinutes(-10)
        };

        var billeteraAnterior = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioAnteriorId,
            SaldoDisponible = 500m,
            SaldoRetenido = 1500m
        };

        var billeteraNueva = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = nuevoUsuarioId,
            SaldoDisponible = 5000m,
            SaldoRetenido = 0m
        };

        var repository = new FakePlaceBidRepository
        {
            Auction = subasta,
            WinningBid = pujaAnterior,
            Wallet = billeteraNueva,
            PreviousWinnerWallet = billeteraAnterior
        };

        var notifier = new FakeBidNotifier();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            new FakeBidAuditWriter());

        var command = new PlaceBidCommand(
            subastaId,
            nuevoUsuarioId,
            2000m);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.Equal(2000m, result.Monto);
        Assert.Equal(2000m, subasta.PrecioActual);

        // El ganador anterior recupera su dinero.
        Assert.Equal(2000m, billeteraAnterior.SaldoDisponible);
        Assert.Equal(0m, billeteraAnterior.SaldoRetenido);

        // El nuevo ganador tiene su oferta retenida.
        Assert.Equal(3000m, billeteraNueva.SaldoDisponible);
        Assert.Equal(2000m, billeteraNueva.SaldoRetenido);

        // La puja anterior deja de ser ganadora.
        Assert.False(pujaAnterior.EsGanadora);

        Assert.Single(repository.AddedBids);
        Assert.True(repository.AddedBids[0].EsGanadora);

        // Una liberación y una retención.
        Assert.Equal(2, repository.AddedLedgerTransactions.Count);

        Assert.Contains(
            repository.AddedLedgerTransactions,
            m => m.Tipo == TipoMovimientoBilletera.Liberacion &&
                 m.Monto == 1500m);

        Assert.Contains(
            repository.AddedLedgerTransactions,
            m => m.Tipo == TipoMovimientoBilletera.Retencion &&
                 m.Monto == 2000m);

        // Se guarda primero el cambio de ganador y luego la nueva puja.
        Assert.Equal(2, repository.SaveChangesCount);

        Assert.Equal(1, repository.CommitTransactionCount);
        Assert.Equal(0, repository.RollbackTransactionCount);
    }

    [Fact]
    public async Task HandleAsync_WhenCurrentWinnerRaisesOwnBid_RetainsOnlyDifference()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();
        var subastaId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var subasta = new Subasta
        {
            Id = subastaId,
            VendedorId = Guid.NewGuid(),
            Estado = EstadoSubasta.Activa,
            PrecioActual = 1500m,
            IncrementoMinimo = 100m,
            FechaInicioUtc = ahora.AddHours(-1),
            FechaFinUtc = ahora.AddHours(1)
        };

        var pujaAnterior = new Puja
        {
            Id = Guid.NewGuid(),
            SubastaId = subastaId,
            UsuarioId = usuarioId,
            Monto = 1500m,
            EsGanadora = true,
            FechaUtc = ahora.AddMinutes(-10)
        };

        var billetera = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            SaldoDisponible = 3500m,
            SaldoRetenido = 1500m
        };

        var repository = new FakePlaceBidRepository
        {
            Auction = subasta,
            WinningBid = pujaAnterior,
            Wallet = billetera
        };

        var notifier = new FakeBidNotifier();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            new FakeBidAuditWriter());

        var command = new PlaceBidCommand(
            subastaId,
            usuarioId,
            2000m);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.Equal(2000m, result.Monto);
        Assert.Equal(2000m, result.PrecioActual);
        Assert.Equal("Liderando", result.Estado);

        // Solo se retienen $500 adicionales.
        Assert.Equal(3000m, billetera.SaldoDisponible);
        Assert.Equal(2000m, billetera.SaldoRetenido);

        // La puja anterior deja de ser ganadora.
        Assert.False(pujaAnterior.EsGanadora);

        Assert.Single(repository.AddedBids);
        Assert.True(repository.AddedBids[0].EsGanadora);

        // Solo existe una retención, sin liberaciones.
        Assert.Single(repository.AddedLedgerTransactions);

        var movimiento = repository.AddedLedgerTransactions[0];

        Assert.Equal(
            TipoMovimientoBilletera.Retencion,
            movimiento.Tipo);

        Assert.Equal(500m, movimiento.Monto);

        // Se guarda el cambio de ganador antes de insertar la nueva puja.
        Assert.Equal(2, repository.SaveChangesCount);

        Assert.Equal(1, repository.CommitTransactionCount);
        Assert.Equal(0, repository.RollbackTransactionCount);
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrencyConflictOccurs_RollsBack()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();
        var subastaId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var subasta = new Subasta
        {
            Id = subastaId,
            VendedorId = Guid.NewGuid(),
            Estado = EstadoSubasta.Activa,
            PrecioActual = 1000m,
            IncrementoMinimo = 100m,
            FechaInicioUtc = ahora.AddHours(-1),
            FechaFinUtc = ahora.AddHours(1)
        };

        var billetera = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            SaldoDisponible = 5000m,
            SaldoRetenido = 0m
        };

        var repository = new FakePlaceBidRepository
        {
            Auction = subasta,
            Wallet = billetera,
            SimulateConcurrencyConflict = true
        };

        var notifier = new FakeBidNotifier();
        var auditWriter = new FakeBidAuditWriter();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            auditWriter);

        var command = new PlaceBidCommand(
            subastaId,
            usuarioId,
            1500m);

        // Act
        var exception = await Assert.ThrowsAsync<BidConcurrencyException>(
            () => handler.HandleAsync(command));

        // Assert
        Assert.Equal("BID_CONFLICT", exception.Code);

        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Equal(0, repository.CommitTransactionCount);
        Assert.Equal(1, repository.RollbackTransactionCount);
        Assert.Equal(0, notifier.NotificationCount);

        // Auditoría del conflicto de concurrencia
        var audit = Assert.Single(auditWriter.Events);

        Assert.Equal("BidConcurrencyConflict", audit.TipoEvento);
        Assert.Equal(command.SubastaId, audit.SubastaId);
        Assert.Equal(command.UsuarioId, audit.UsuarioId);
        Assert.Contains("BID_CONFLICT", audit.Detalle);
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrencyAndRollbackFail_PreservesConcurrencyError()
    {
        // Arrange
        var subastaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var subasta = new Subasta
        {
            Id = subastaId,
            VendedorId = Guid.NewGuid(),
            Estado = EstadoSubasta.Activa,
            PrecioActual = 1000m,
            IncrementoMinimo = 100m,
            FechaInicioUtc = ahora.AddHours(-1),
            FechaFinUtc = ahora.AddHours(1)
        };

        var billetera = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            SaldoDisponible = 5000m,
            SaldoRetenido = 0m
        };

        var repository = new FakePlaceBidRepository
        {
            Auction = subasta,
            Wallet = billetera,
            SimulateConcurrencyConflict = true,
            SimulateRollbackFailure = true
        };

        var notifier = new FakeBidNotifier();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            new FakeBidAuditWriter());

        var command = new PlaceBidCommand(
            subastaId,
            usuarioId,
            2000m);

        // Act
        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => handler.HandleAsync(command));

        // Assert

        // Se conservaron los dos errores originales.
        Assert.Equal(2, exception.InnerExceptions.Count);

        Assert.IsType<BidPersistenceConcurrencyException>(
            exception.InnerExceptions[0]);

        var rollbackFailure = Assert.IsType<InvalidOperationException>(
            exception.InnerExceptions[1]);

        Assert.Equal(
            "Error simulado durante el rollback.",
            rollbackFailure.Message);

        // Se intentó revertir la transacción.
        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Equal(1, repository.RollbackTransactionCount);

        // No se confirmó la puja ni se notificó.
        Assert.Equal(0, repository.CommitTransactionCount);
        Assert.Equal(0, notifier.NotificationCount);
    }

    [Fact]
    public async Task HandleAsync_WhenCommitFails_RollsBackAndDoesNotNotify()
    {
        // Arrange
        var subastaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var subasta = new Subasta
        {
            Id = subastaId,
            VendedorId = Guid.NewGuid(),
            Estado = EstadoSubasta.Activa,
            PrecioActual = 1000m,
            IncrementoMinimo = 100m,
            FechaInicioUtc = ahora.AddHours(-1),
            FechaFinUtc = ahora.AddHours(1)
        };

        var billetera = new Billetera
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            SaldoDisponible = 5000m,
            SaldoRetenido = 0m
        };

        var repository = new FakePlaceBidRepository
        {
            Auction = subasta,
            Wallet = billetera,
            SimulateCommitFailure = true
        };

        var notifier = new FakeBidNotifier(repository.Events);

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            new FakeBidAuditWriter());

        var command = new PlaceBidCommand(
            subastaId,
            usuarioId,
            1500m);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(command));

        // Assert
        Assert.Equal(
            "Error simulado durante el Commit.",
            exception.Message);

        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Equal(1, repository.CommitTransactionCount);
        Assert.Equal(1, repository.RollbackTransactionCount);

        Assert.Equal(0, notifier.NotificationCount);
        Assert.Equal(
            new[] { "CommitFailed" },
            repository.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(100.123)]
    public async Task HandleAsync_WhenAmountIsInvalid_RegistersRejectionWithoutStartingTransaction(
    decimal monto)
    {
        // Arrange
        var repository = new FakePlaceBidRepository();
        var notifier = new FakeBidNotifier();
        var auditWriter = new FakeBidAuditWriter();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            auditWriter);

        var command = new PlaceBidCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            monto);

        // Act
        var exception = await Assert.ThrowsAsync<BidValidationException>(
            () => handler.HandleAsync(command));

        // Assert
        Assert.Equal(0, repository.BeginTransactionCount);
        Assert.Equal(0, repository.CommitTransactionCount);
        Assert.Equal(0, repository.RollbackTransactionCount);
        Assert.Equal(0, notifier.NotificationCount);

        var audit = Assert.Single(auditWriter.Events);

        Assert.Equal("BidRejected", audit.TipoEvento);
        Assert.Equal(command.SubastaId, audit.SubastaId);
        Assert.Equal(command.UsuarioId, audit.UsuarioId);
        Assert.Contains(exception.Code, audit.Detalle);
    }

    [Fact]
    public async Task HandleAsync_WhenAuditWriteFails_PropagatesAuditFailure()
    {
        // Arrange
        var repository = new FakePlaceBidRepository();
        var notifier = new FakeBidNotifier();

        var auditWriter = new FakeBidAuditWriter
        {
            SimulateWriteFailure = true
        };

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            auditWriter);

        var command = new PlaceBidCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1500m);

        // Act
        var exception = await Assert.ThrowsAsync<BidAuditException>(
            () => handler.HandleAsync(command));

        // Assert

        // Verificamos que se conserve el rechazo original.
        Assert.IsType<BidNotFoundException>(
            exception.OriginalRejection);

        Assert.Equal(
            "AUCTION_NOT_FOUND",
            exception.OriginalRejection.Code);

        // Verificamos que se conserve el error de auditoría.
        var auditFailure = Assert.IsType<InvalidOperationException>(
            exception.InnerException);

        Assert.Equal(
            "Error simulado al guardar la auditoría.",
            auditFailure.Message);

        // La transacción de la puja fue revertida.
        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.RollbackTransactionCount);
        Assert.Equal(0, repository.CommitTransactionCount);

        // No se registró la auditoría ni se notificó una puja.
        Assert.Empty(auditWriter.Events);
        Assert.Equal(0, notifier.NotificationCount);
    }

    [Fact]
    public async Task HandleAsync_WhenBusinessRejectionAndRollbackFail_PreservesBothErrors()
    {
        // Arrange
        var repository = new FakePlaceBidRepository
        {
            SimulateRollbackFailure = true
        };

        var notifier = new FakeBidNotifier();
        var auditWriter = new FakeBidAuditWriter();

        var handler = new PlaceBidCommandHandler(
            repository,
            TimeProvider.System,
            notifier,
            auditWriter);

        // La subasta no existe en el repositorio.
        var command = new PlaceBidCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1500m);

        // Act
        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => handler.HandleAsync(command));

        // Assert
        Assert.Equal(2, exception.InnerExceptions.Count);

        var rejection = Assert.IsType<BidNotFoundException>(
            exception.InnerExceptions[0]);

        Assert.Equal("AUCTION_NOT_FOUND", rejection.Code);

        var rollbackFailure = Assert.IsType<InvalidOperationException>(
            exception.InnerExceptions[1]);

        Assert.Equal(
            "Error simulado durante el rollback.",
            rollbackFailure.Message);

        Assert.Equal(1, repository.BeginTransactionCount);
        Assert.Equal(1, repository.RollbackTransactionCount);
        Assert.Equal(0, repository.CommitTransactionCount);

        // No afirmamos que el rechazo quedó revertido y auditado.
        Assert.Empty(auditWriter.Events);

        Assert.Equal(0, notifier.NotificationCount);
    }

    [Fact]
    public async Task HandleAsync_WhenAuctionDoesNotExist_PersistsAuditAfterRollback()
    {
        // Arrange
        await using var database = await BidTestDatabase.CreateAsync();

        await using var context = database.CreateContext();

        var notifier = new FakeBidNotifier();

        var (handler, _) = database.CreatePlaceBidHandler(
            context,
            notifier);

        var command = new PlaceBidCommand(
            Guid.NewGuid(), // Subasta inexistente
            BidTestDatabase.BidderOneId,
            150m);

        // Act
        var exception = await Assert.ThrowsAsync<BidNotFoundException>(
            () => handler.HandleAsync(command));

        // Assert
        Assert.Equal("AUCTION_NOT_FOUND", exception.Code);

        // Consultamos mediante otro contexto para verificar
        // que el evento quedó persistido.
        await using var verificationContext = database.CreateContext();

        var audit = await verificationContext.AuditoriaLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal("BidRejected", audit.TipoEvento);
        Assert.Null(audit.SubastaId);
        Assert.Contains(command.SubastaId.ToString(), audit.Detalle);
        Assert.Equal(command.UsuarioId, audit.UsuarioId);
        Assert.Contains("AUCTION_NOT_FOUND", audit.Detalle);

        // La puja rechazada no debe generar movimientos.
        Assert.Empty(await verificationContext.Pujas.ToListAsync());
        Assert.Empty(await verificationContext.TransaccionLedgers.ToListAsync());

        Assert.Equal(0, notifier.NotificationCount);
    }

    [Fact]
    public async Task HandleAsync_WhenFundsAreInsufficient_PersistsRejectionAfterRollback()
    {
        // Arrange
        await using var database = await BidTestDatabase.CreateAsync(
            bidderOneBalance: 100m);

        await using var context = database.CreateContext();

        var notifier = new FakeBidNotifier();

        var (handler, _) = database.CreatePlaceBidHandler(
            context,
            notifier);

        var command = new PlaceBidCommand(
            BidTestDatabase.AuctionId,
            BidTestDatabase.BidderOneId,
            150m);

        // Act
        var exception = await Assert.ThrowsAnyAsync<BidRuleException>(
        () => handler.HandleAsync(command));

        // Assert
        Assert.NotEqual("AUCTION_NOT_FOUND", exception.Code);

        await using var verificationContext = database.CreateContext();

        var audit = await verificationContext.AuditoriaLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal("BidRejected", audit.TipoEvento);
        Assert.Equal(BidTestDatabase.AuctionId, audit.SubastaId);
        Assert.Equal(BidTestDatabase.BidderOneId, audit.UsuarioId);
        Assert.Contains(exception.Code, audit.Detalle);

        Assert.Empty(await verificationContext.Pujas.ToListAsync());
        Assert.Empty(await verificationContext.TransaccionLedgers.ToListAsync());

        var wallet = await verificationContext.Billeteras
            .SingleAsync(w => w.UsuarioId == BidTestDatabase.BidderOneId);

        Assert.Equal(100m, wallet.SaldoDisponible);
        Assert.Equal(0m, wallet.SaldoRetenido);

        Assert.Equal(0, notifier.NotificationCount);
    }

    [Fact]
    public async Task AuditLog_WhenPersisted_CannotBeModifiedOrDeleted()
    {
        await using var database = await BidTestDatabase.CreateAsync();

        Guid auditId;

        // Crear una auditoría persistente.
        await using (var context = database.CreateContext())
        {
            var audit = new AuditoriaLog
            {
                Id = Guid.NewGuid(),
                SubastaId = BidTestDatabase.AuctionId,
                UsuarioId = BidTestDatabase.BidderOneId,
                TipoEvento = "BidRejected",
                Detalle = "Evento original de prueba.",
                FechaUtc = database.Now
            };

            context.AuditoriaLogs.Add(audit);
            await context.SaveChangesAsync();

            auditId = audit.Id;
        }

        // Intentar modificarla.
        await using (var context = database.CreateContext())
        {
            var audit = await context.AuditoriaLogs.SingleAsync(
                a => a.Id == auditId);

            audit.Detalle = "Evento modificado.";

            await Assert.ThrowsAnyAsync<Exception>(
                () => context.SaveChangesAsync());
        }

        // Intentar eliminarla.
        await using (var context = database.CreateContext())
        {
            var audit = await context.AuditoriaLogs.SingleAsync(
                a => a.Id == auditId);

            context.AuditoriaLogs.Remove(audit);

            await Assert.ThrowsAnyAsync<Exception>(
                () => context.SaveChangesAsync());
        }

        // Comprobar que el registro original permanece intacto.
        await using (var context = database.CreateContext())
        {
            var audit = await context.AuditoriaLogs
                .AsNoTracking()
                .SingleAsync(a => a.Id == auditId);

            Assert.Equal("Evento original de prueba.", audit.Detalle);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenConcurrencyConflictOccurs_PersistsAudit()
    {
        await using var database = await BidTestDatabase.CreateAsync();

        var repository = new FakePlaceBidRepository
        {
            Auction = new Subasta
            {
                Id = BidTestDatabase.AuctionId,
                VendedorId = BidTestDatabase.SellerId,
                Estado = EstadoSubasta.Activa,
                PrecioActual = 100m,
                IncrementoMinimo = 10m,
                FechaInicioUtc = database.Now.AddHours(-1),
                FechaFinUtc = database.Now.AddMinutes(10)
            },
            Wallet = new Billetera
            {
                Id = BidTestDatabase.BidderOneWalletId,
                UsuarioId = BidTestDatabase.BidderOneId,
                SaldoDisponible = 1000m,
                SaldoRetenido = 0m
            },
            SimulateConcurrencyConflict = true
        };

        var notifier = new FakeBidNotifier();

        var auditWriter = new BidAuditWriter(
            new TestDbContextFactory(database.Options));

        var handler = new PlaceBidCommandHandler(
            repository,
            new FixedTimeProvider(database.Now),
            notifier,
            auditWriter);

        var command = new PlaceBidCommand(
            BidTestDatabase.AuctionId,
            BidTestDatabase.BidderOneId,
            150m);

        var exception = await Assert.ThrowsAsync<BidConcurrencyException>(
            () => handler.HandleAsync(command));

        Assert.Equal("BID_CONFLICT", exception.Code);
        Assert.Equal(1, repository.RollbackTransactionCount);
        Assert.Equal(0, repository.CommitTransactionCount);
        Assert.Equal(0, notifier.NotificationCount);

        await using var verificationContext = database.CreateContext();

        var audit = await verificationContext.AuditoriaLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal("BidConcurrencyConflict", audit.TipoEvento);
        Assert.Equal(command.SubastaId, audit.SubastaId);
        Assert.Equal(command.UsuarioId, audit.UsuarioId);
        Assert.Contains("BID_CONFLICT", audit.Detalle);

        Assert.Empty(await verificationContext.Pujas.ToListAsync());
        Assert.Empty(await verificationContext.TransaccionLedgers.ToListAsync());
    }
}
