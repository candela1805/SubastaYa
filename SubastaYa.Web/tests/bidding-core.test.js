const test = require("node:test");
const assert = require("node:assert/strict");

const biddingCore = require("../bidding-core.js");

test("calcula montos en centavos sin deriva de punto flotante", () => {
    assert.equal(biddingCore.toMinorUnits("0.10"), 10n);
    assert.equal(biddingCore.toMinorUnits("0.20"), 20n);
    assert.equal(
        biddingCore.calculateSuggestedBid("0.10", "0.20"),
        "0.30"
    );
    assert.equal(
        biddingCore.calculateSuggestedBid("100.01", "0.09"),
        "100.10"
    );
});

test("admite el máximo decimal(18,2) y rechaza sus desbordes", () => {
    const maximumMinorUnits = 999999999999999999n;

    assert.equal(
        biddingCore.toMinorUnits("9999999999999999.99"),
        maximumMinorUnits
    );
    assert.equal(
        biddingCore.fromMinorUnits(maximumMinorUnits),
        "9999999999999999.99"
    );
    assert.throws(
        () => biddingCore.toMinorUnits("10000000000000000.00"),
        RangeError
    );
    assert.throws(
        () => biddingCore.calculateSuggestedBid(
            "9999999999999999.99",
            "0.01"
        ),
        RangeError
    );
});

test("formatMoney conserva centavos y aplica el formato monetario local", () => {
    assert.equal(
        biddingCore.formatMoney("1234567.8"),
        "$\u00a01.234.567,80"
    );
    assert.equal(
        biddingCore.formatMoney("0.10", { currencySymbol: "ARS" }),
        "ARS\u00a00,10"
    );
});

test("buildBidRequest arma URL, cuerpo y credenciales del POST", () => {
    const request = biddingCore.buildBidRequest(
        "http://localhost:5000/",
        "auction/id",
        "100.3"
    );

    assert.equal(
        request.url,
        "http://localhost:5000/api/auctions/auction%2Fid/bids"
    );
    assert.equal(request.options.method, "POST");
    assert.equal(request.options.credentials, "include");
    assert.deepEqual(request.options.headers, {
        "Content-Type": "application/json"
    });
    assert.equal(request.options.body, '{"monto":100.30}');
});

test("buildRoomStateRequest arma un GET autenticado y sin caché", () => {
    const request = biddingCore.buildRoomStateRequest(
        "http://localhost:5000/",
        "auction/id"
    );

    assert.equal(
        request.url,
        "http://localhost:5000/api/auctions/auction%2Fid/bids/state"
    );
    assert.deepEqual(request.options, {
        method: "GET",
        cache: "no-store",
        credentials: "include"
    });
});

test("el estado UI mantiene las fases de carga y error", () => {
    const initialState = biddingCore.createBidUiState();
    const loadingState = biddingCore.reduceBidUiState(initialState, {
        type: "ROOM_LOADING",
        message: "Consultando estado"
    });
    const errorState = biddingCore.reduceBidUiState(loadingState, {
        type: "ROOM_FAILED",
        message: "No hay conexión"
    });

    assert.deepEqual(
        {
            phase: loadingState.phase,
            feedback: loadingState.feedback,
            tone: loadingState.feedbackTone,
            blocked: loadingState.blocked
        },
        {
            phase: "loading",
            feedback: "Consultando estado",
            tone: "info",
            blocked: true
        }
    );
    assert.deepEqual(
        {
            phase: errorState.phase,
            feedback: errorState.feedback,
            tone: errorState.feedbackTone,
            blocked: errorState.blocked
        },
        {
            phase: "error",
            feedback: "No hay conexión",
            tone: "error",
            blocked: true
        }
    );
    assert.strictEqual(
        biddingCore.reduceBidUiState(errorState, { type: "TICK" }),
        errorState
    );
});

test("el error de una puja queda visible y vuelve a habilitar el envío", () => {
    const submittingState = biddingCore.reduceBidUiState(
        biddingCore.createBidUiState(),
        {
            type: "BID_SUBMITTING"
        }
    );
    const failedState = biddingCore.reduceBidUiState(submittingState, {
        type: "BID_FAILED",
        message: "Saldo insuficiente"
    });

    assert.equal(submittingState.phase, "submitting");
    assert.equal(submittingState.feedbackTone, "info");
    assert.equal(failedState.phase, "idle");
    assert.equal(failedState.feedback, "Saldo insuficiente");
    assert.equal(failedState.feedbackTone, "error");
    assert.equal(failedState.blocked, false);
});

test("normaliza Sin puja, Liderando y Superado sin usar pseudónimos", () => {
    const cases = [
        ["SinPuja", { key: "none", label: "Sin puja" }],
        [" liderando ", { key: "leading", label: "Liderando" }],
        ["SUPERADO", { key: "outbid", label: "Superado" }]
    ];

    for (const [input, expected] of cases) {
        assert.deepEqual(
            biddingCore.normalizeBidderStatus(input),
            expected
        );
    }
});

test("el coordinador ante 409 hace POST, refresca una vez y no reintenta", async () => {
    const calls = [];
    const conflictResponse = {
        status: 409,
        code: "BID_CONFLICT"
    };
    const refreshedState = {
        precioActual: 130,
        pujaMinimaSiguiente: 140
    };
    const coordinator = biddingCore.createBidSubmissionCoordinator({
        async sendBid(request) {
            calls.push(["POST", request]);
            return conflictResponse;
        },
        async refreshRoomState(response) {
            calls.push(["GET", response]);
            return refreshedState;
        }
    });

    const result = await coordinator.submit({ monto: "130.00" });

    assert.deepEqual(calls, [
        ["POST", { monto: "130.00" }],
        ["GET", conflictResponse]
    ]);
    assert.equal(result.kind, "conflict");
    assert.strictEqual(result.response, conflictResponse);
    assert.strictEqual(result.refreshResult, refreshedState);
    assert.equal(coordinator.isSubmitting, false);
    assert.equal(
        calls.filter(([method]) => method === "POST").length,
        1
    );
});

test("una respuesta distinta de 409 no refresca el estado", async () => {
    let sends = 0;
    let refreshes = 0;
    const response = {
        status: 422,
        code: "INSUFFICIENT_BALANCE"
    };
    const coordinator = biddingCore.createBidSubmissionCoordinator({
        async sendBid() {
            sends += 1;
            return response;
        },
        async refreshRoomState() {
            refreshes += 1;
        }
    });

    const result = await coordinator.submit({ monto: "130.00" });

    assert.equal(sends, 1);
    assert.equal(refreshes, 0);
    assert.equal(result.kind, "response");
    assert.strictEqual(result.response, response);
});

test("dos envíos simultáneos producen una sola petición", async () => {
    let releaseRequest;
    const pendingResponse = new Promise(resolve => {
        releaseRequest = resolve;
    });
    let sends = 0;
    let refreshes = 0;
    const coordinator = biddingCore.createBidSubmissionCoordinator({
        async sendBid() {
            sends += 1;
            return pendingResponse;
        },
        async refreshRoomState() {
            refreshes += 1;
        }
    });

    const firstSubmission = coordinator.submit({ monto: "130.00" });
    const duplicateResult = await coordinator.submit({ monto: "130.00" });

    assert.equal(coordinator.isSubmitting, true);
    assert.equal(duplicateResult.kind, "duplicate");
    assert.equal(sends, 1);
    assert.equal(refreshes, 0);

    releaseRequest({ status: 200 });
    const firstResult = await firstSubmission;

    assert.equal(firstResult.kind, "response");
    assert.equal(coordinator.isSubmitting, false);
    assert.equal(sends, 1);
});

test("mergeAuctionWithRoomState aplica la fecha anti-sniping y el nuevo mínimo", () => {
    const auction = {
        id: "auction-1",
        title: "Notebook",
        currentBid: "100.00",
        minimumIncrement: "10.00",
        nextMinimum: "110.00",
        endDateUtc: "2026-09-15T18:10:00Z"
    };
    const mergedAuction = biddingCore.mergeAuctionWithRoomState(auction, {
        precioActual: "110.00",
        incrementoMinimo: "10.00",
        pujaMinimaSiguiente: "120.00",
        fechaFinUtc: "2026-09-15T18:12:00Z",
        estadoSubasta: "Activa",
        estadoPostor: "Liderando"
    });

    assert.equal(auction.endDateUtc, "2026-09-15T18:10:00Z");
    assert.equal(mergedAuction.currentBid, "110.00");
    assert.equal(mergedAuction.minimumIncrement, "10.00");
    assert.equal(mergedAuction.nextMinimum, "120.00");
    assert.equal(mergedAuction.endDateUtc, "2026-09-15T18:12:00Z");
    assert.equal(mergedAuction.status, "Activa");
    assert.deepEqual(mergedAuction.bidderStatus, {
        key: "leading",
        label: "Liderando"
    });
});
