const API_BASE_URL = "http://localhost:5000";
const DEFAULT_AUCTION_IMAGE = createPlaceholderImage();
const BID_POLL_INTERVAL_MS = 5000;
const BID_STATE_REQUEST_TIMEOUT_MS = 8000;
const BID_STATE_RETRY_DELAYS_MS = [1000, 2500, 5000];
const biddingCore = window.SubastaYaBidding;

if (!biddingCore) {
    throw new Error("No se pudo cargar el módulo seguro de pujas.");
}

const auctionStateNames = {
    1: "Programada",
    2: "Activa",
    3: "Finalizada",
    4: "Desierta"
};

let catalogAuctions = [];
let activeAuction = null;
let bidHistory = [];
let hubConnection = null;
let countdownTimer = null;
let catalogCountdownTimer = null;
let bidPollingTimer = null;
let bidPollingInProgress = false;
let joinedAuctionId = null;
let bidSubmissionCoordinator = null;
let bidStateRequestSequence = 0;
let bidHistoryRequestSequence = 0;
let liveRoomGeneration = 0;
let bidStateAbortController = null;
let bidStateRetryTimer = null;
let bidStateRetryAttempt = 0;
let activeBidSubmissionContext = null;
let deferredPersonalStateRefresh = null;
let bidUiState = biddingCore.createBidUiState();
const notifiedExtensions = new Set();


/* =========================
   INICIALIZACIÓN
   ========================= */

document.addEventListener("DOMContentLoaded", async () => {
    const initialAuctionId = getAuctionIdFromHash();

    if (!initialAuctionId) {
        await handleRoute(false);
    }

    configureNavigation();
    configureVisualControls();
    configureFilters();
    configureLiveRoom();

    if (initialAuctionId) {
        await loadAuctions();
        await handleRoute(false);
        return;
    }

    await loadAuctions();
});


/* =========================
   NAVEGACIÓN
   ========================= */

function configureNavigation() {
    document.querySelectorAll("[data-view]").forEach(link => {
        link.addEventListener("click", async event => {
            event.preventDefault();

            const targetHash = `#${link.dataset.view}`;

            if (location.hash === targetHash) {
                await handleRoute(false);
                return;
            }

            location.hash = targetHash;
        });
    });

    window.addEventListener("hashchange", async () => {
        await handleRoute(false);
    });
}

function getViewFromHash() {
    const view = location.hash.slice(1);

    return ["catalog", "activities", "wallet"].includes(view)
        ? view
        : "catalog";
}

async function handleRoute(scroll = true) {

    const auctionId = getAuctionIdFromHash();

    if (auctionId) {
        const auction = catalogAuctions.find(item => item.id === auctionId);

        if (canOpenAuction(auction)) {
            await openLiveRoom(auction, scroll);
            return;
        }

        await leaveLiveRoom();
        history.replaceState(null, "", "#catalog");
        showView("catalog", scroll);
        showToast(
            "Sala no disponible",
            "La subasta ya no existe o ya no se encuentra activa.",
            "warning"
        );
        return;
    }

    const view = getViewFromHash();

    showView(view, scroll);

    if (view === "wallet") {
        await loadWalletBalance();
    }

    await leaveLiveRoom();
}

function getAuctionIdFromHash() {
    const match = location.hash.match(/^#auction\/([0-9a-f-]{36})$/i);

    return match ? match[1].toLowerCase() : null;
}

function showView(name, scroll = true) {
    document.querySelectorAll(".app-view").forEach(view => {
        view.classList.toggle("active", view.dataset.page === name);
    });

    document.querySelectorAll(".main-nav [data-view]").forEach(link => {
        link.classList.toggle("active", link.dataset.view === name);
    });

    const menu = document.getElementById("navbarNav");

    if (menu?.classList.contains("show") && window.bootstrap) {
        bootstrap.Collapse.getOrCreateInstance(menu).hide();
    }

    if (scroll) {
        window.scrollTo({
            top: 0,
            behavior: "smooth"
        });
    }
}


/* =========================
   CATÁLOGO
   ========================= */

function configureFilters() {
    const form = document.getElementById("auction-filters");

    form.addEventListener("change", () => {
        loadAuctions();
    });

    form.addEventListener("submit", event => {
        event.preventDefault();
        loadAuctions();
    });

    form.addEventListener("reset", () => {
        window.setTimeout(loadAuctions, 0);
    });
}

async function loadAuctions() {
    const container = document.getElementById("auctions-container");

    window.clearInterval(catalogCountdownTimer);
    catalogCountdownTimer = null;
    container.innerHTML = createLoadingState("Cargando subastas");

    try {
        const parameters = createAuctionQuery();
        const response = await fetch(
            `${API_BASE_URL}/api/auctions?${parameters}`,
            {
                cache: "no-store",
                credentials: "include"
            }
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        const result = await response.json();
        catalogAuctions = result.items.map(normalizeAuction);
        renderAuctions(catalogAuctions);
        startCatalogCountdown();
    } catch (error) {
        container.innerHTML = createErrorState(
            "No se pudo cargar el catálogo",
            error.message
        );
    }
}

function createAuctionQuery() {
    const form = new FormData(document.getElementById("auction-filters"));
    const parameters = new URLSearchParams({
        page: "1",
        pageSize: "50"
    });

    form.forEach((value, key) => {
        if (String(value).trim()) {
            parameters.set(key, value);
        }
    });

    return parameters;
}

function normalizeAuction(auction) {
    return {
        id: auction.id,
        title: auction.titulo,
        description: auction.descripcion,
        category: auction.categoria,
        currentBid: auction.precioActual,
        minimumIncrement: auction.incrementoMinimo,
        nextMinimum: biddingCore.calculateSuggestedBid(
            auction.precioActual,
            auction.incrementoMinimo
        ),
        endDateUtc: auction.fechaFinUtc,
        status: auctionStateNames[auction.estado] ?? auction.estado,
        image: auction.imagenUrl || DEFAULT_AUCTION_IMAGE
    };
}

function renderAuctions(auctions) {
    const container = document.getElementById("auctions-container");

    container.innerHTML = "";

    if (auctions.length === 0) {
        container.innerHTML = createEmptyState(
            "Sin subastas para mostrar",
            "No se encontraron resultados para los filtros seleccionados."
        );
        return;
    }

    auctions.forEach(auction => {
        const remaining = getRemainingMilliseconds(auction.endDateUtc);
        const ending = auction.status === "Activa" && remaining <= 60000;
        const column = document.createElement("div");

        column.className = "col-12 col-md-6 col-xl-4";
        column.innerHTML = `
            <article class="auction-card" data-auction-card-id="${auction.id}">
                <div class="auction-image-wrap">
                    <img
                        src="${escapeHtml(auction.image)}"
                        alt="${escapeHtml(auction.title)}"
                    >
                    <span
                        class="auction-status ${ending ? "ending" : ""}"
                        data-auction-status
                    >
                        ${ending ? "Por terminar" : escapeHtml(auction.status)}
                    </span>
                    <button class="favorite-button" aria-label="Agregar a favoritos">
                        ♡
                    </button>
                    <span
                        class="time-badge ${ending ? "ending" : ""}"
                        data-auction-countdown
                    >
                        ◴ ${formatRemainingTime(remaining)}
                    </span>
                </div>
                <div class="auction-body">
                    <h2>${escapeHtml(auction.title)}</h2>
                    <span class="category-badge">${escapeHtml(auction.category)}</span>
                    <div class="auction-values">
                        <div>
                            <span>Oferta actual</span>
                            <strong class="current-price" data-auction-price>
                                ${money(auction.currentBid)}
                            </strong>
                        </div>
                        <div class="text-end">
                            <span>Incremento mínimo</span>
                            <strong>${money(auction.minimumIncrement)}</strong>
                        </div>
                    </div>
                    <button
                        class="bid-button"
                        type="button"
                        data-auction-id="${auction.id}"
                        ${auction.status !== "Activa" || remaining <= 0 ? "disabled" : ""}
                    >
                        ⚒&nbsp; Ingresar a pujar
                    </button>
                </div>
            </article>
        `;

        column.querySelector("[data-auction-id]").addEventListener("click", () => {
            enterLiveRoom(auction.id);
        });

        configureImageFallback(column.querySelector("img"));

        container.appendChild(column);
    });
}

function startCatalogCountdown() {
    window.clearInterval(catalogCountdownTimer);
    updateCatalogCountdowns();
    catalogCountdownTimer = window.setInterval(updateCatalogCountdowns, 1000);
}

function updateCatalogCountdowns() {
    catalogAuctions.forEach(auction => {
        const card = document.querySelector(
            `[data-auction-card-id="${auction.id}"]`
        );

        if (!card) {
            return;
        }

        const remaining = getRemainingMilliseconds(auction.endDateUtc);
        const countdown = card.querySelector("[data-auction-countdown]");
        const status = card.querySelector("[data-auction-status]");
        const button = card.querySelector("[data-auction-id]");
        const ending = auction.status === "Activa" && remaining > 0 && remaining <= 60000;

        countdown.textContent = `◴ ${formatRemainingTime(remaining)}`;
        countdown.classList.toggle("ending", ending || remaining <= 0);
        status.classList.toggle("ending", ending || remaining <= 0);

        if (remaining <= 0 && auction.status === "Activa") {
            auction.status = "Finalizada";
        }

        status.textContent = ending ? "Por terminar" : auction.status;
        button.disabled = !canOpenAuction(auction);
    });
}


/* =========================
   SALA DE PUJAS EN VIVO
   ========================= */

function configureLiveRoom() {
    document.getElementById("live-back-button").addEventListener("click", () => {
        location.hash = "catalog";
    });

    bidSubmissionCoordinator = biddingCore.createBidSubmissionCoordinator({
        sendBid: request => fetch(request.url, request.options),
        refreshRoomState: response => refreshRoomStateAfterConflict(
            response,
            activeBidSubmissionContext
        )
    });

    document.getElementById("bid-form").addEventListener("submit", placeBid);
}

async function enterLiveRoom(auctionId) {
    const auction = catalogAuctions.find(item => item.id === auctionId);

    if (!canOpenAuction(auction)) {
        showToast("Puja rechazada", "No se encontró la subasta seleccionada.", "danger");
        return;
    }

    history.pushState(null, "", `#auction/${auction.id}`);
    await openLiveRoom(auction, true);
}

async function openLiveRoom(auction, scroll) {
    if (activeAuction?.id !== auction.id) {
        await leaveLiveRoom();
    }

    activeAuction = auction;
    liveRoomGeneration += 1;
    const roomContext = getCurrentRoomContext();

    cancelBidStateRetry();
    invalidateBidStateRequest();
    bidHistoryRequestSequence += 1;
    deferredPersonalStateRefresh = null;
    bidHistory = [];
    bidUiState = biddingCore.reduceBidUiState(
        biddingCore.createBidUiState(),
        {
            type: "ROOM_LOADING"
        }
    );

    showView("live", scroll);
    renderLiveAuction();
    startCountdown();

    await Promise.all([
        loadBidHistory({ roomContext }),
        loadBidRoomState({
            roomContext,
            skipLoadingState: true,
            forceSuggestedAmount: true
        }),
        connectToAuctionHub(roomContext)
    ]);
}

function renderLiveAuction({ forceSuggestedAmount = false } = {}) {
    if (!activeAuction) {
        return;
    }

    const image = document.getElementById("live-image");
    const nextMinimum = getNextMinimum();

    image.classList.remove("placeholder-image");
    image.src = activeAuction.image;
    image.alt = activeAuction.title;
    configureImageFallback(image);
    document.getElementById("live-title").textContent = activeAuction.title;
    document.getElementById("live-category").textContent = activeAuction.category;
    document.getElementById("live-current-price").textContent = money(activeAuction.currentBid);
    document.getElementById("live-minimum-increment").textContent = money(
        activeAuction.minimumIncrement
    );
    document.getElementById("live-next-minimum").textContent = money(nextMinimum);
    document.getElementById("live-end-date").textContent = formatExactDate(
        activeAuction.endDateUtc
    );

    updateBidMinimum(forceSuggestedAmount);
    updateAuctionAvailability();
    updateCountdown();
}

function startCountdown() {
    window.clearInterval(countdownTimer);
    updateCountdown();
    countdownTimer = window.setInterval(updateCountdown, 1000);
}

function updateCountdown() {
    if (!activeAuction) {
        return;
    }

    const countdown = document.getElementById("live-countdown");
    const remaining = getRemainingMilliseconds(activeAuction.endDateUtc);

    countdown.textContent = formatRemainingTime(remaining);
    countdown.classList.remove("warning");
    countdown.classList.toggle("critical", remaining <= 60000);

    if (remaining <= 0) {
        activeAuction.status = "Finalizada";
        updateAuctionAvailability();
        window.clearInterval(countdownTimer);
        countdownTimer = null;
    }
}

function updateAuctionAvailability() {
    if (!activeAuction) {
        return;
    }

    const active =
        activeAuction.status === "Activa" &&
        getRemainingMilliseconds(activeAuction.endDateUtc) > 0;
    const status = document.getElementById("live-status");

    status.textContent = active ? "Activa" : activeAuction.status;
    status.classList.toggle("ending", !active);
    renderBidInteraction(active);
}

function updateBidMinimum(forceSuggestedAmount = false) {
    const minimum = getNextMinimum();
    const input = document.getElementById("bid-amount");
    let currentInputIsBelowMinimum = true;

    if (input.value) {
        try {
            currentInputIsBelowMinimum =
                biddingCore.toMinorUnits(input.value) <
                biddingCore.toMinorUnits(minimum);
        } catch {
            currentInputIsBelowMinimum = true;
        }
    }

    input.min = minimum;

    if (forceSuggestedAmount || !input.value || currentInputIsBelowMinimum) {
        input.value = minimum;
    }

    document.getElementById("bid-minimum-help").textContent =
        `Oferta mínima permitida: ${money(minimum)}`;
}

function getNextMinimum() {
    if (activeAuction.nextMinimum !== undefined) {
        return biddingCore.fromMinorUnits(
            biddingCore.toMinorUnits(activeAuction.nextMinimum)
        );
    }

    return biddingCore.calculateSuggestedBid(
        activeAuction.currentBid,
        activeAuction.minimumIncrement
    );
}

function renderBidInteraction(auctionIsActive = canOpenAuction(activeAuction)) {
    const status = document.getElementById("bidder-status");
    const statusLabel = document.getElementById("bidder-status-label");
    const feedback = document.getElementById("bid-feedback");
    const form = document.getElementById("bid-form");
    const input = document.getElementById("bid-amount");
    const button = document.getElementById("place-bid-button");
    const submitting = Boolean(bidSubmissionCoordinator?.isSubmitting) ||
        bidUiState.phase === "submitting";
    const loading = ["loading", "refreshing"].includes(bidUiState.phase);
    const controlsDisabled =
        !auctionIsActive || bidUiState.blocked || submitting;

    status.dataset.state = loading
        ? "loading"
        : bidUiState.bidderStatus.key;
    statusLabel.textContent = loading
        ? "Cargando…"
        : bidUiState.bidderStatus.label;
    feedback.textContent = bidUiState.feedback;
    feedback.dataset.tone = bidUiState.feedbackTone;
    form.setAttribute("aria-busy", String(submitting || loading));
    input.disabled = controlsDisabled;
    button.disabled = controlsDisabled;
    button.textContent = submitting
        ? "Procesando…"
        : "⚒  Realizar puja";
}

function getCurrentRoomContext() {
    if (!activeAuction) {
        return null;
    }

    return {
        auctionId: activeAuction.id,
        generation: liveRoomGeneration
    };
}

function isCurrentRoomContext(roomContext) {
    return Boolean(
        roomContext &&
        activeAuction?.id === roomContext.auctionId &&
        liveRoomGeneration === roomContext.generation
    );
}

function invalidateBidStateRequest() {
    bidStateRequestSequence += 1;

    if (bidStateAbortController) {
        const controller = bidStateAbortController;

        bidStateAbortController = null;
        controller.abort();
    }
}

function cancelBidStateRetry() {
    window.clearTimeout(bidStateRetryTimer);
    bidStateRetryTimer = null;
    bidStateRetryAttempt = 0;
}

function scheduleBidStateRetry(roomContext) {
    if (
        !isCurrentRoomContext(roomContext) ||
        bidStateRetryTimer ||
        bidPollingTimer
    ) {
        return;
    }

    if (bidStateRetryAttempt >= BID_STATE_RETRY_DELAYS_MS.length) {
        bidUiState = {
            ...bidUiState,
            phase: "idle",
            blocked: false,
            feedback:
                `${bidUiState.feedback} Podés intentar pujar con los últimos datos visibles.`,
            feedbackTone: "warning"
        };
        renderBidInteraction();
        return;
    }

    const attempt = bidStateRetryAttempt + 1;
    const delay = BID_STATE_RETRY_DELAYS_MS[bidStateRetryAttempt];

    bidStateRetryAttempt = attempt;
    bidStateRetryTimer = window.setTimeout(() => {
        bidStateRetryTimer = null;

        if (!isCurrentRoomContext(roomContext)) {
            return;
        }

        bidUiState = biddingCore.reduceBidUiState(bidUiState, {
            type: "ROOM_LOADING",
            message:
                `Reintentando actualizar la sala (${attempt}/${BID_STATE_RETRY_DELAYS_MS.length})…`
        });
        renderBidInteraction();

        void loadBidRoomState({
            roomContext,
            isRetry: true,
            skipLoadingState: true
        });
    }, delay);
}

function deferPersonalStateRefresh(roomContext) {
    if (isCurrentRoomContext(roomContext)) {
        deferredPersonalStateRefresh = {
            ...roomContext
        };
    }
}

function consumeDeferredPersonalStateRefresh(roomContext) {
    if (
        deferredPersonalStateRefresh?.auctionId === roomContext?.auctionId &&
        deferredPersonalStateRefresh?.generation === roomContext?.generation
    ) {
        deferredPersonalStateRefresh = null;
    }
}

function flushDeferredPersonalStateRefresh(roomContext) {
    if (
        !isCurrentRoomContext(roomContext) ||
        bidSubmissionCoordinator?.isSubmitting ||
        deferredPersonalStateRefresh?.auctionId !== roomContext.auctionId ||
        deferredPersonalStateRefresh?.generation !== roomContext.generation
    ) {
        return;
    }

    deferredPersonalStateRefresh = null;
    void loadBidRoomState({
        roomContext,
        skipLoadingState: true
    });
}

async function loadBidRoomState({
    roomContext = getCurrentRoomContext(),
    isRetry = false,
    skipLoadingState = false,
    forceSuggestedAmount = false,
    feedbackMessage = "",
    feedbackTone = "neutral"
} = {}) {
    if (!isCurrentRoomContext(roomContext)) {
        return null;
    }

    const auctionId = roomContext.auctionId;

    if (!isRetry) {
        cancelBidStateRetry();
    }

    invalidateBidStateRequest();
    const requestSequence = bidStateRequestSequence;
    const abortController = new AbortController();
    let requestTimedOut = false;

    bidStateAbortController = abortController;
    consumeDeferredPersonalStateRefresh(roomContext);

    const requestTimeout = window.setTimeout(() => {
        requestTimedOut = true;
        abortController.abort();
    }, BID_STATE_REQUEST_TIMEOUT_MS);

    if (!skipLoadingState) {
        bidUiState = biddingCore.reduceBidUiState(bidUiState, {
            type: "ROOM_LOADING",
            message: feedbackMessage || undefined,
            tone: feedbackTone === "neutral" ? undefined : feedbackTone
        });
        renderBidInteraction();
    }

    try {
        const request = biddingCore.buildRoomStateRequest(
            API_BASE_URL,
            auctionId
        );
        const response = await fetch(request.url, {
            ...request.options,
            signal: abortController.signal
        });

        if (!response.ok) {
            const error = new Error(await readApiError(response));
            error.status = response.status;
            throw error;
        }

        const roomState = await response.json();

        if (
            requestSequence !== bidStateRequestSequence ||
            !isCurrentRoomContext(roomContext)
        ) {
            return null;
        }

        const mergedAuction = biddingCore.mergeAuctionWithRoomState(
            activeAuction,
            roomState
        );

        mergedAuction.status =
            auctionStateNames[roomState.estadoSubasta] ??
            roomState.estadoSubasta;
        Object.assign(activeAuction, mergedAuction);
        updateCatalogAuctionSnapshot(activeAuction);
        cancelBidStateRetry();

        bidUiState = biddingCore.reduceBidUiState(bidUiState, {
            type: "ROOM_LOADED",
            bidderStatus: roomState.estadoPostor,
            message: feedbackMessage,
            tone: feedbackTone
        });
        renderLiveAuction({
            forceSuggestedAmount
        });

        return roomState;
    } catch (error) {
        if (
            requestSequence !== bidStateRequestSequence ||
            !isCurrentRoomContext(roomContext)
        ) {
            return null;
        }

        const errorMessage = requestTimedOut
            ? "La consulta superó el tiempo máximo de espera."
            : error.message;
        const message = feedbackMessage
            ? `${feedbackMessage} No se pudo refrescar la sala: ${errorMessage}`
            : `No se pudo actualizar tu estado: ${errorMessage}`;

        bidUiState = biddingCore.reduceBidUiState(bidUiState, {
            type: "ROOM_FAILED",
            message
        });
        renderBidInteraction();
        scheduleBidStateRetry(roomContext);

        return null;
    } finally {
        window.clearTimeout(requestTimeout);

        if (bidStateAbortController === abortController) {
            bidStateAbortController = null;
        }
    }
}

async function refreshRoomStateAfterConflict(response, roomContext) {
    const message = await readApiError(response);

    if (!isCurrentRoomContext(roomContext)) {
        return {
            message,
            roomState: null,
            stale: true
        };
    }

    bidUiState = biddingCore.reduceBidUiState(bidUiState, {
        type: "BID_CONFLICT",
        message: `${message} Actualizando precio y mínimo…`
    });
    renderBidInteraction();

    const roomState = await loadBidRoomState({
        roomContext,
        skipLoadingState: true,
        forceSuggestedAmount: true,
        feedbackMessage:
            `${message} Los datos fueron actualizados; revisá la nueva puja mínima.`,
        feedbackTone: "warning"
    });

    return {
        message,
        roomState,
        stale: !isCurrentRoomContext(roomContext)
    };
}

async function loadBidHistory({
    silent = false,
    roomContext = getCurrentRoomContext()
} = {}) {
    if (!isCurrentRoomContext(roomContext)) {
        return;
    }

    const container = document.getElementById("bid-history");
    const auctionId = roomContext.auctionId;
    const requestSequence = ++bidHistoryRequestSequence;

    if (!silent) {
        container.innerHTML = createLoadingState("Cargando historial");
    }

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/${auctionId}/bids`,
            {
                cache: "no-store",
                credentials: "include"
            }
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        const history = await response.json();

        if (
            requestSequence !== bidHistoryRequestSequence ||
            !isCurrentRoomContext(roomContext)
        ) {
            return;
        }

        bidHistory = history;
        renderBidHistory();
    } catch (error) {
        if (
            requestSequence === bidHistoryRequestSequence &&
            isCurrentRoomContext(roomContext) &&
            !silent
        ) {
            container.innerHTML = createErrorState(
                "No se pudo cargar el historial",
                error.message
            );
        }
    }
}

function renderBidHistory() {
    const container = document.getElementById("bid-history");

    if (bidHistory.length === 0) {
        container.innerHTML = createEmptyState(
            "Todavía no hay pujas",
            "Sé la primera persona en ofertar por esta subasta."
        );
        return;
    }

    bidHistory.sort((first, second) => {
        return new Date(first.fechaUtc) - new Date(second.fechaUtc);
    });

    container.innerHTML = bidHistory.map(bid => `
        <article class="history-entry">
            <div>
                <strong>${escapeHtml(bid.pseudonimo)}</strong>
                <time datetime="${bid.fechaUtc}">${formatExactDate(bid.fechaUtc)}</time>
            </div>
            <b>${money(bid.monto)}</b>
        </article>
    `).join("");

    container.scrollTop = container.scrollHeight;
}

async function placeBid(event) {
    event.preventDefault();

    if (
        !activeAuction ||
        !bidSubmissionCoordinator ||
        bidSubmissionCoordinator.isSubmitting ||
        bidUiState.phase === "submitting"
    ) {
        return;
    }

    const roomContext = getCurrentRoomContext();
    let request;

    try {
        request = biddingCore.buildBidRequest(
            API_BASE_URL,
            roomContext.auctionId,
            document.getElementById("bid-amount").value
        );
    } catch (error) {
        bidUiState = biddingCore.reduceBidUiState(bidUiState, {
            type: "BID_FAILED",
            message: error.message
        });
        renderBidInteraction();
        showToast("Puja rechazada", error.message, "danger");
        return;
    }

    cancelBidStateRetry();
    invalidateBidStateRequest();
    bidHistoryRequestSequence += 1;
    bidUiState = biddingCore.reduceBidUiState(bidUiState, {
        type: "BID_SUBMITTING"
    });
    renderBidInteraction();
    activeBidSubmissionContext = roomContext;

    try {
        const result = await bidSubmissionCoordinator.submit(request);

        if (
            result.kind === "duplicate" ||
            !isCurrentRoomContext(roomContext)
        ) {
            return;
        }

        if (result.kind === "conflict") {
            if (!result.refreshResult.stale) {
                showBidError(409, result.refreshResult.message);
            }
            return;
        }

        const response = result.response;

        if (!response.ok) {
            const message = await readApiError(response);

            if (!isCurrentRoomContext(roomContext)) {
                return;
            }

            bidUiState = biddingCore.reduceBidUiState(bidUiState, {
                type: "BID_FAILED",
                message
            });
            renderBidInteraction();
            showBidError(response.status, message);
            return;
        }

        const bid = await response.json();

        if (!isCurrentRoomContext(roomContext)) {
            return;
        }

        applyBid(bid);
        const confirmation =
            `Tu oferta de ${money(bid.monto)} fue registrada.`;

        bidUiState = biddingCore.reduceBidUiState(bidUiState, {
            type: "BID_SUCCEEDED",
            message: confirmation
        });
        renderBidInteraction();

        await loadBidRoomState({
            roomContext,
            skipLoadingState: true,
            forceSuggestedAmount: true,
            feedbackMessage: confirmation,
            feedbackTone: "success"
        });

        if (isCurrentRoomContext(roomContext)) {
            showToast("Puja confirmada", confirmation, "success");
        }
    } catch (error) {
        if (isCurrentRoomContext(roomContext)) {
            bidUiState = biddingCore.reduceBidUiState(bidUiState, {
                type: "BID_FAILED",
                message: error.message
            });
            renderBidInteraction();
            showToast("Error inesperado", error.message, "danger");
        }
    } finally {
        if (activeBidSubmissionContext === roomContext) {
            activeBidSubmissionContext = null;
        }

        if (isCurrentRoomContext(roomContext)) {
            updateAuctionAvailability();
            flushDeferredPersonalStateRefresh(roomContext);
        } else if (activeAuction) {
            updateAuctionAvailability();
        }
    }
}

function showBidError(status, message) {
    const normalized = message.toLowerCase();

    if (status === 409) {
        showToast("Conflicto de concurrencia", message, "warning");
    } else if (normalized.includes("saldo") || normalized.includes("fondos")) {
        showToast("Fondos insuficientes", message, "danger");
    } else {
        showToast("Puja rechazada", message, "danger");
    }
}

function applyBid(bid) {
    if (!activeAuction || bid.subastaId !== activeAuction.id) {
        return;
    }

    activeAuction.currentBid = bid.precioActual;
    activeAuction.endDateUtc = bid.fechaFinUtc;
    activeAuction.nextMinimum = bid.pujaMinimaSiguiente ??
        biddingCore.calculateSuggestedBid(
            bid.precioActual,
            activeAuction.minimumIncrement
        );

    if (bid.incrementoMinimo !== undefined) {
        activeAuction.minimumIncrement = bid.incrementoMinimo;
    }

    updateCatalogAuctionSnapshot(activeAuction);

    if (!bidHistory.some(existingBid => existingBid.id === bid.id)) {
        bidHistory.push({
            id: bid.id,
            monto: bid.monto,
            pseudonimo: bid.pseudonimo,
            fechaUtc: bid.fechaUtc
        });
        renderBidHistory();
    }

    renderLiveAuction();

    if (bid.subastaExtendida && !notifiedExtensions.has(bid.id)) {
        notifiedExtensions.add(bid.id);
        showToast(
            "Subasta extendida",
            "La puja ingresó en el último minuto y el cierre se extendió dos minutos.",
            "warning"
        );
    }
}

function updateCatalogAuctionSnapshot(sourceAuction) {
    const catalogAuction = catalogAuctions.find(auction => {
        return auction.id === sourceAuction.id;
    });

    if (catalogAuction) {
        catalogAuction.currentBid = sourceAuction.currentBid;
        catalogAuction.minimumIncrement = sourceAuction.minimumIncrement;
        catalogAuction.nextMinimum = sourceAuction.nextMinimum;
        catalogAuction.endDateUtc = sourceAuction.endDateUtc;
        catalogAuction.status = sourceAuction.status;

        const price = document.querySelector(
            `[data-auction-card-id="${sourceAuction.id}"] [data-auction-price]`
        );

        if (price) {
            price.textContent = money(sourceAuction.currentBid);
        }
    }
}


/* =========================
   SIGNALR
   ========================= */

async function connectToAuctionHub(
    roomContext = getCurrentRoomContext()
) {
    const connectionStatus = document.getElementById("connection-status");

    if (!isCurrentRoomContext(roomContext)) {
        return;
    }

    if (!window.signalR) {
        connectionStatus.textContent = "Actualizando cada 5 s";
        startBidPolling();
        return;
    }

    if (!hubConnection) {
        hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(`${API_BASE_URL}/hubs/auctions`, {
                withCredentials: true
            })
            .withAutomaticReconnect()
            .build();

        hubConnection.on("BidPlaced", bid => {
            void handleRealtimeBidPlaced(bid);
        });
        hubConnection.on("AuctionStateChanged", applyAuctionState);
        hubConnection.onreconnecting(() => {
            connectionStatus.textContent = "Reconectando…";
            startBidPolling();
        });
        hubConnection.onreconnected(() => {
            void handleAuctionHubReconnected(connectionStatus);
        });
        hubConnection.onclose(() => {
            connectionStatus.textContent = "Actualizando cada 5 s";
            startBidPolling();
        });
    }

    try {
        const auctionId = roomContext.auctionId;

        if (hubConnection.state === signalR.HubConnectionState.Disconnected) {
            await hubConnection.start();
        }

        if (!isCurrentRoomContext(roomContext)) {
            return;
        }

        if (joinedAuctionId && joinedAuctionId !== auctionId) {
            await hubConnection.invoke("LeaveAuction", joinedAuctionId);
        }

        if (joinedAuctionId !== auctionId) {
            await hubConnection.invoke("JoinAuction", auctionId);
            joinedAuctionId = auctionId;
        }

        connectionStatus.textContent = "Conectado";
        stopBidPolling();
    } catch (error) {
        if (!isCurrentRoomContext(roomContext)) {
            return;
        }

        connectionStatus.textContent = "Actualizando cada 5 s";
        startBidPolling();
        showToast("Tiempo real no disponible", error.message, "warning");
    }
}

async function handleAuctionHubReconnected(connectionStatus) {
    connectionStatus.textContent = "Conectado";
    stopBidPolling();

    if (!activeAuction) {
        return;
    }

    const roomContext = getCurrentRoomContext();
    const auctionId = roomContext.auctionId;

    try {
        await hubConnection.invoke("JoinAuction", auctionId);

        if (!isCurrentRoomContext(roomContext)) {
            return;
        }

        joinedAuctionId = auctionId;
        await Promise.all([
            loadBidHistory({
                roomContext,
                silent: true
            }),
            loadBidRoomState({
                roomContext,
                skipLoadingState: true
            })
        ]);
    } catch (error) {
        if (!isCurrentRoomContext(roomContext)) {
            return;
        }

        connectionStatus.textContent = "Actualizando cada 5 s";
        startBidPolling();
        showToast("Reconexión incompleta", error.message, "warning");
    }
}

async function handleRealtimeBidPlaced(bid) {
    if (!activeAuction || bid.subastaId !== activeAuction.id) {
        return;
    }

    const roomContext = getCurrentRoomContext();

    cancelBidStateRetry();
    invalidateBidStateRequest();
    bidHistoryRequestSequence += 1;

    if (!isCurrentRoomContext(roomContext)) {
        return;
    }

    applyBid(bid);

    if (
        bidSubmissionCoordinator?.isSubmitting ||
        ["submitting", "refreshing"].includes(bidUiState.phase)
    ) {
        deferPersonalStateRefresh(roomContext);
        return;
    }

    await loadBidRoomState({
        roomContext,
        skipLoadingState: true
    });
}

function startBidPolling() {
    if (!activeAuction || bidPollingTimer) {
        return;
    }

    cancelBidStateRetry();
    bidPollingTimer = window.setInterval(async () => {
        if (
            !activeAuction ||
            bidPollingInProgress ||
            bidSubmissionCoordinator?.isSubmitting
        ) {
            return;
        }

        bidPollingInProgress = true;
        const roomContext = getCurrentRoomContext();

        try {
            await Promise.all([
                loadBidRoomState({
                    roomContext,
                    skipLoadingState: true
                }),
                loadBidHistory({
                    roomContext,
                    silent: true
                })
            ]);
        } finally {
            bidPollingInProgress = false;
        }
    }, BID_POLL_INTERVAL_MS);
}

function stopBidPolling() {
    window.clearInterval(bidPollingTimer);
    bidPollingTimer = null;
    bidPollingInProgress = false;
}

function applyAuctionState(update) {
    if (!activeAuction || update.subastaId !== activeAuction.id) {
        return;
    }

    activeAuction.status = auctionStateNames[update.estado] ?? update.estado;
    activeAuction.endDateUtc = update.fechaFinUtc;
    renderLiveAuction();
}

async function leaveLiveRoom() {
    window.clearInterval(countdownTimer);
    countdownTimer = null;
    stopBidPolling();
    cancelBidStateRetry();
    invalidateBidStateRequest();
    bidHistoryRequestSequence += 1;
    liveRoomGeneration += 1;
    deferredPersonalStateRefresh = null;

    const auctionIdToLeave = joinedAuctionId;

    joinedAuctionId = null;
    activeAuction = null;
    bidHistory = [];
    bidUiState = biddingCore.createBidUiState();

    if (
        auctionIdToLeave &&
        hubConnection?.state === window.signalR?.HubConnectionState.Connected
    ) {
        try {
            await hubConnection.invoke("LeaveAuction", auctionIdToLeave);
        } catch {
            // La reconexión automática resolverá la membresía del grupo.
        }
    }
}


/* =========================
   MI BILLETERA
   ========================= */

async function loadWalletBalance() {
    const total = document.getElementById("wallet-total");
    const retained = document.getElementById("wallet-retained");
    const available = document.getElementById("wallet-available");

    try {
        if (!total || !retained || !available) {
            throw new Error("No se encontraron las tres tarjetas de saldo en el DOM.");
        }

        const walletUrl = `${API_BASE_URL}/api/wallet/balance`;

        const response = await fetch(
            walletUrl,
            {
                cache: "no-store",
                credentials: "include"
            }
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        const balance = await response.json();

        total.textContent = money(balance.total);
        retained.textContent = money(balance.retenido);
        available.textContent = money(balance.disponible);
    } catch (error) {
        if (total) {
            total.textContent = "$ —";
        }

        if (retained) {
            retained.textContent = "$ —";
        }

        if (available) {
            available.textContent = "$ —";
        }

        showToast(
            "Billetera no disponible",
            error.message,
            "danger"
        );
    }
}


/* =========================
   CONTROLES VISUALES EXISTENTES
   ========================= */

function configureVisualControls() {
    document.querySelectorAll(".amount-options button").forEach(button => {
        button.addEventListener("click", event => {
            event.preventDefault();

            document.querySelectorAll(".amount-options button").forEach(item => {
                item.classList.remove("selected");
            });

            button.classList.add("selected");

            const input = document.getElementById("deposit-amount");
            input.value = button.dataset.amount;

            if (!button.dataset.amount) {
                input.focus();
            }
        });
    });

    document.querySelectorAll(".tabs").forEach(group => {
        group.querySelectorAll("button").forEach(button => {
            button.addEventListener("click", () => {
                group.querySelectorAll("button").forEach(tab => {
                    tab.classList.remove("active");
                });

                button.classList.add("active");
            });
        });
    });
}


/* =========================
   UTILIDADES
   ========================= */

function money(value) {
    return biddingCore.formatMoney(value);
}

function getRemainingMilliseconds(endDateUtc) {
    return Math.max(0, new Date(endDateUtc).getTime() - Date.now());
}

function canOpenAuction(auction) {
    return Boolean(
        auction &&
        auction.status === "Activa" &&
        getRemainingMilliseconds(auction.endDateUtc) > 0
    );
}

function createPlaceholderImage() {
    const svg = `
        <svg xmlns="http://www.w3.org/2000/svg" width="900" height="520">
            <rect width="100%" height="100%" fill="#e9edf3"/>
            <g fill="#667085" text-anchor="middle" font-family="Arial, sans-serif">
                <text x="450" y="245" font-size="54">⚖</text>
                <text x="450" y="300" font-size="22">Imagen no disponible</text>
            </g>
        </svg>
    `;

    return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`;
}

function configureImageFallback(image) {
    if (!image || image.dataset.fallbackConfigured === "true") {
        return;
    }

    image.dataset.fallbackConfigured = "true";
    image.addEventListener("error", () => {
        image.src = DEFAULT_AUCTION_IMAGE;
        image.classList.add("placeholder-image");
    });
}

function formatRemainingTime(milliseconds) {
    const totalSeconds = Math.ceil(milliseconds / 1000);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    return [hours, minutes, seconds]
        .map(value => String(value).padStart(2, "0"))
        .join(":");
}

function formatExactDate(value) {
    return new Intl.DateTimeFormat("es-AR", {
        dateStyle: "short",
        timeStyle: "medium"
    }).format(new Date(value));
}

async function readApiError(response) {
    const contentType = response.headers.get("content-type") ?? "";

    if (contentType.includes("application/json")) {
        const body = await response.json();

        if (body.message) {
            return body.message;
        }

        if (body.errors) {
            return Object.values(body.errors).flat().join(" ");
        }
    }

    return await response.text() || `Error HTTP ${response.status}`;
}

function escapeHtml(value) {
    const element = document.createElement("div");
    element.textContent = value ?? "";
    return element.innerHTML;
}

function createLoadingState(title) {
    return `
        <div class="empty-state">
            <i>◴</i>
            <strong>${escapeHtml(title)}</strong>
        </div>
    `;
}

function createEmptyState(title, description) {
    return `
        <div class="empty-state">
            <i>⌁</i>
            <strong>${escapeHtml(title)}</strong>
            <p>${escapeHtml(description)}</p>
        </div>
    `;
}

function createErrorState(title, description) {
    return `
        <div class="empty-state error-state">
            <i>!</i>
            <strong>${escapeHtml(title)}</strong>
            <p>${escapeHtml(description)}</p>
        </div>
    `;
}

function showToast(title, message, type) {
    const container = document.getElementById("toast-container");
    const toastElement = document.createElement("div");

    toastElement.className = `toast app-toast toast-${type}`;
    toastElement.setAttribute("role", "status");
    toastElement.innerHTML = `
        <div class="toast-header">
            <strong class="me-auto">${escapeHtml(title)}</strong>
            <button
                type="button"
                class="btn-close"
                data-bs-dismiss="toast"
                aria-label="Cerrar"
            ></button>
        </div>
        <div class="toast-body">${escapeHtml(message)}</div>
    `;

    container.appendChild(toastElement);

    const toast = new bootstrap.Toast(toastElement, {
        delay: 5000
    });

    toastElement.addEventListener("hidden.bs.toast", () => {
        toastElement.remove();
    });

    toast.show();
}
