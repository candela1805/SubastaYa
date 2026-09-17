const API_BASE_URL = "http://localhost:5000";
const SESSION_STORAGE_KEY =
    "subastaya_session";

let currentSession =
    loadUserSession();
const DEFAULT_AUCTION_IMAGE = createPlaceholderImage();
const BID_POLL_INTERVAL_MS = 5000;
const BID_STATE_REQUEST_TIMEOUT_MS = 8000;
const BID_STATE_RETRY_DELAYS_MS = [1000, 2500, 5000];
const NEW_CATEGORY_VALUE = "__new_category__";
const PAYMENT_METHODS = [
    { id: "card", icon: "▣", name: "Tarjeta de débito/crédito", description: "Acreditación representativa con tarjeta." },
    { id: "transfer", icon: "⇄", name: "Transferencia bancaria", description: "Transferencia desde una cuenta bancaria." },
    { id: "deposit", icon: "$", name: "Depósito", description: "Depósito representativo de fondos." }
];
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
let availableCategories = [];
let walletTransactions = [];
let walletSection = "summary";
let preferredPaymentMethod = "card";
let myPublications = [];
let publicationFilter = "all";
let publicationPendingDeletion = null;
let myBidActivities = [];
let purchaseFilter = "EnCurso";
let activityCountdownTimer = null;
let activityRefreshTimer = null;
const activityJoinedAuctionIds = new Set();
const activityExpirationRefreshes = new Set();
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

function getAuthenticatedRequestOptions(
    options = {}
) {

    const headers = {
        ...(options.headers || {})
    };


    if (currentSession?.userId) {

        headers["X-User-Id"] =
            currentSession.userId;
    }


    return {
        ...options,
        credentials: "include",
        headers
    };
}


/* =========================
   INICIALIZACIÓN
   ========================= */

document.addEventListener("DOMContentLoaded", async () => {
    configureAuthentication();

    if (currentSession?.userId) {
        await refreshCurrentUserProfile();
    }

    configureWallet();
    configureCategoryControls();
    await loadCategories();

    const initialAuctionId = getAuctionIdFromHash();

    if (!initialAuctionId) {
        await handleRoute(false);
    }

    configureNavigation();
    configureVisualControls();
    configureFilters();
    configureLiveRoom();
    configureAuctionCreation();
    configureActivities();

    if (initialAuctionId) {
        await loadAuctions();
        await handleRoute(false);
        return;
    }

    await loadAuctions();
});;


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

    const auctionId =
        getAuctionIdFromHash();

    const view =
        getViewFromHash();


    // Sin sesión solamente se permite Catálogo.
    if (
        !currentSession?.userId &&
        (
            auctionId ||
            view === "activities" ||
            view === "wallet"
        )
    ) {

        await leaveActivityAuctionGroups();
        await leaveLiveRoom();

        history.replaceState(
            null,
            "",
            "#catalog"
        );

        showView(
            "catalog",
            scroll
        );

        return;
    }


    if (auctionId) {
        await leaveActivityAuctionGroups();
        const auction =
            catalogAuctions.find(
                item =>
                    item.id === auctionId
            );


        if (auction) {

            await openLiveRoom(
                auction,
                scroll
            );

            return;
        }


        await leaveLiveRoom();

        history.replaceState(
            null,
            "",
            "#catalog"
        );

        showView(
            "catalog",
            scroll
        );

        showToast(
            "Sala no disponible",
            "La subasta ya no existe o ya no se encuentra activa.",
            "warning"
        );

        return;
    }

    if (view !== "activities") {
        await leaveActivityAuctionGroups();
    }

    await leaveLiveRoom();

    showView(
        view,
        scroll
    );
    if (view === "wallet") {
        await loadWalletSection(walletSection);
    }


    if (view === "activities") {
        await Promise.all([
            loadMyBidActivities(),
            loadMyPublications()
        ]);
    }
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
        sendBid: request => fetch(
            request.url,
            getAuthenticatedRequestOptions(request.options)
        ),
        refreshRoomState: response => refreshRoomStateAfterConflict(
            response,
            activeBidSubmissionContext
        )
    });

    document.getElementById("bid-form").addEventListener("submit", placeBid);
}

async function enterLiveRoom(auctionId) {
    if (!currentSession?.userId) {

        showToast(
            "Iniciá sesión",
            "Tenés que ingresar a tu cuenta para participar de una subasta.",
            "warning"
        );

        bootstrap.Modal
            .getOrCreateInstance(
                document.getElementById(
                    "login-modal"
                )
            )
            .show();

        return;
    }


    const auction =
        catalogAuctions.find(
            item => item.id === auctionId
        );


    if (!canOpenAuction(auction)) {

        showToast(
            "Puja rechazada",
            "No se encontró la subasta seleccionada.",
            "danger"
        );

        return;
    }


    history.pushState(
        null,
        "",
        `#auction/${auction.id}`
    );

    await openLiveRoom(
        auction,
        true
    );
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
        const response = await fetch(
            request.url,
            getAuthenticatedRequestOptions({
                ...request.options,
                signal: abortController.signal
            })
        );
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

    ensureAuctionHubConnection();

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

function ensureAuctionHubConnection() {
    if (hubConnection || !window.signalR) {
        return;
    }

    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(`${API_BASE_URL}/hubs/auctions`, {
            withCredentials: true
        })
        .withAutomaticReconnect()
        .build();

    hubConnection.on("BidPlaced", bid => {
        void handleRealtimeBidPlaced(bid);
        scheduleBidActivitiesRefresh(bid.subastaId);
    });
    hubConnection.on("AuctionStateChanged", update => {
        applyAuctionState(update);
        scheduleBidActivitiesRefresh(update.subastaId);
    });
    hubConnection.onreconnecting(() => {
        const connectionStatus = document.getElementById("connection-status");
        if (connectionStatus) {
            connectionStatus.textContent = "Reconectando…";
        }
        startBidPolling();
    });
    hubConnection.onreconnected(() => {
        const connectionStatus = document.getElementById("connection-status");
        if (activeAuction && connectionStatus) {
            void handleAuctionHubReconnected(connectionStatus);
        }
        void rejoinActivityAuctionGroups();
    });
    hubConnection.onclose(() => {
        const connectionStatus = document.getElementById("connection-status");
        if (connectionStatus) {
            connectionStatus.textContent = "Actualizando cada 5 s";
        }
        startBidPolling();
    });
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
            getAuthenticatedRequestOptions({
                cache: "no-store"
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        const balance = await response.json();

        renderWalletBalance(balance);
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

async function loadWalletTransactions() {
    ["wallet-recent-movements", "wallet-all-movements"].forEach(id => {
        const container = document.getElementById(id);
        if (container) {
            container.innerHTML = createLoadingState("Cargando movimientos");
        }
    });

    const response = await fetch(
        `${API_BASE_URL}/api/wallet/transactions`,
        getAuthenticatedRequestOptions({ cache: "no-store" })
    );

    if (!response.ok) {
        throw new Error(await readApiError(response));
    }

    walletTransactions = await response.json();
    renderWalletMovements(
        document.getElementById("wallet-recent-movements"),
        walletTransactions.slice(0, 4)
    );
    renderWalletMovements(
        document.getElementById("wallet-all-movements"),
        walletTransactions
    );
}

async function loadRetainedFunds() {
    const container = document.getElementById("wallet-retained-list");
    container.innerHTML = createLoadingState("Cargando fondos retenidos");

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/wallet/retained-funds`,
            getAuthenticatedRequestOptions({ cache: "no-store" })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        renderRetainedFunds(await response.json());
    } catch (error) {
        container.innerHTML = createErrorState(
            "No se pudieron cargar los fondos retenidos",
            error.message
        );
    }
}

async function loadWalletSection(section) {
    if (section === "summary") {
        await Promise.all([
            loadWalletBalance(),
            loadWalletTransactions().catch(error => {
                renderWalletMovementError(error);
            })
        ]);
        return;
    }

    if (section === "transactions") {
        try {
            await loadWalletTransactions();
        } catch (error) {
            renderWalletMovementError(error);
        }
        return;
    }

    if (section === "retained") {
        await loadRetainedFunds();
    }
}

function configureWallet() {
    try {
        preferredPaymentMethod = localStorage.getItem(
            "subastaya-wallet-method"
        ) || "card";
        if (!PAYMENT_METHODS.some(method => method.id === preferredPaymentMethod)) {
            preferredPaymentMethod = "card";
        }
    } catch {
        preferredPaymentMethod = "card";
    }

    document.querySelectorAll("[data-wallet-target]").forEach(button => {
        button.addEventListener("click", async () => {
            await showWalletSection(button.dataset.walletTarget);
        });
    });

    document.querySelectorAll("[data-deposit-form]").forEach(form => {
        populatePaymentSelect(form.elements.metodo);
        renderPaymentFlow(form);
        form.addEventListener("submit", submitWalletDeposit);
        form.elements.monto.addEventListener("input", () => {
            updatePaymentFlowAmount(form);
            updateDepositButton(form);
        });
        form.elements.metodo.addEventListener("change", () => {
            renderPaymentFlow(form);
            updateDepositButton(form);
        });
        form.querySelectorAll("[data-amount]").forEach(button => {
            button.addEventListener("click", () => {
                form.querySelectorAll("[data-amount]").forEach(item => {
                    item.classList.toggle("selected", item === button);
                });
                form.elements.monto.value = button.dataset.amount;
                if (!button.dataset.amount) {
                    form.elements.monto.focus();
                }
                updateDepositButton(form);
            });
        });
        updateDepositButton(form);
    });

    renderPaymentMethods();
}

async function showWalletSection(section) {
    walletSection = section;
    document.querySelectorAll("[data-wallet-section]").forEach(panel => {
        panel.classList.toggle("active", panel.dataset.walletSection === section);
    });
    document.querySelectorAll(".wallet-side-nav [data-wallet-target]").forEach(button => {
        button.classList.toggle("active", button.dataset.walletTarget === section);
    });
    await loadWalletSection(section);
}

function populatePaymentSelect(select) {
    select.replaceChildren(new Option("Seleccionar método", ""));
    PAYMENT_METHODS.forEach(method => {
        select.add(new Option(method.name, method.id));
    });
    select.value = preferredPaymentMethod;
}

function updateDepositButton(form) {
    const amount = Number(form.elements.monto.value);
    const button = form.querySelector("button[type='submit']");
    const simulationFields = [
        ...form.querySelectorAll("[data-payment-flow] input[required]")
    ];
    button.disabled = !(
        Number.isFinite(amount) &&
        amount > 0 &&
        form.elements.metodo.value &&
        simulationFields.every(input => input.checkValidity())
    );
}

async function submitWalletDeposit(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const amount = Number(form.elements.monto.value);
    const method = form.elements.metodo.value;

    form.elements.monto.setCustomValidity(
        Number.isFinite(amount) && amount > 0
            ? ""
            : "Ingresá un monto mayor a cero."
    );
    form.elements.metodo.setCustomValidity(
        method ? "" : "Seleccioná un método de pago."
    );

    if (!form.reportValidity()) {
        return;
    }

    if (!validatePaymentSimulation(form, method)) {
        showToast(
            "Revisá los datos ingresados",
            "Completá correctamente los datos de la simulación.",
            "warning"
        );
        return;
    }

    const button = form.querySelector("button[type='submit']");
    const status = form.querySelector("[data-payment-status]");
    button.disabled = true;
    const messages = paymentFlowMessages(method);
    button.textContent = messages.processing;
    status.innerHTML = `<span class="payment-spinner"></span>${messages.processing}`;

    try {
        await waitForPaymentSimulation();
        const response = await fetch(
            `${API_BASE_URL}/api/wallet/deposit`,
            getAuthenticatedRequestOptions({
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ monto: amount })
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        const balance = await response.json();
        renderWalletBalance(balance);
        await loadWalletTransactions();
        status.innerHTML = `<span class="payment-success">✓</span>${messages.success}`;
        clearSensitivePaymentFields(form);
        form.reset();
        form.elements.metodo.value = preferredPaymentMethod;
        form.querySelectorAll("[data-amount]").forEach(item => {
            item.classList.remove("selected");
        });
        renderPaymentFlow(form);
        status.innerHTML = `<span class="payment-success">✓</span>${messages.success}`;
        updateDepositButton(form);
        showToast(
            messages.success,
            `${money(amount)} se acreditaron en tu billetera.`,
            "success"
        );
    } catch (error) {
        status.textContent = "No pudimos acreditar la operación.";
        showToast(
            "No se pudo acreditar",
            "Intentá nuevamente en unos instantes.",
            "danger"
        );
    } finally {
        button.textContent = button.dataset.actionLabel;
        updateDepositButton(form);
    }
}

function renderPaymentFlow(form) {
    const method = form.elements.metodo.value;
    const container = form.querySelector("[data-payment-flow]");
    const status = form.querySelector("[data-payment-status]");
    const amount = Number(form.elements.monto.value) || 0;
    const submitButton = form.querySelector("button[type='submit']");

    clearSensitivePaymentFields(form);
    status.textContent = "";

    if (method === "card") {
        container.innerHTML = `
            <section class="payment-simulation-card">
                <h3>Datos de la tarjeta</h3>
                <div class="card-data-grid">
                    <label class="wallet-form-field full-width">
                        <span>Número de tarjeta</span>
                        <input
                            name="cardNumber"
                            inputmode="numeric"
                            autocomplete="off"
                            maxlength="19"
                            placeholder="0000 0000 0000 0000"
                            pattern="[0-9 ]{19}"
                            required
                        >
                    </label>
                    <label class="wallet-form-field full-width">
                        <span>Nombre del titular</span>
                        <input
                            name="cardHolder"
                            autocomplete="off"
                            maxlength="80"
                            placeholder="NOMBRE APELLIDO"
                            required
                        >
                    </label>
                    <label class="wallet-form-field">
                        <span>Vencimiento</span>
                        <input
                            name="cardExpiry"
                            inputmode="numeric"
                            autocomplete="off"
                            maxlength="5"
                            placeholder="MM/AA"
                            pattern="(0[1-9]|1[0-2])/[0-9]{2}"
                            required
                        >
                    </label>
                    <label class="wallet-form-field">
                        <span>CVV</span>
                        <input
                            name="cardCvv"
                            type="password"
                            inputmode="numeric"
                            autocomplete="off"
                            maxlength="4"
                            placeholder="000"
                            pattern="[0-9]{3,4}"
                            required
                        >
                    </label>
                </div>
                <small class="academic-warning">
                    Simulación académica. No ingreses datos bancarios reales.
                </small>
                <button type="button" class="payment-cancel" data-cancel-payment>
                    Cancelar simulación
                </button>
            </section>
        `;
        configureCardSimulation(form);
    } else if (method === "transfer") {
        container.innerHTML = createTransferSimulation(amount);
        configureCopyButtons(container);
    } else if (method === "deposit") {
        container.innerHTML = createDepositSimulation(amount);
    } else {
        container.innerHTML = "";
    }

    submitButton.dataset.actionLabel = {
        card: "Confirmar pago",
        transfer: "Ya realicé la transferencia",
        deposit: "Ya realicé el depósito"
    }[method] || "Confirmar depósito";
    submitButton.textContent = submitButton.dataset.actionLabel;

    container.querySelector("[data-cancel-payment]")?.addEventListener(
        "click",
        () => cancelPaymentSimulation(form)
    );
    container.oninput = () => updateDepositButton(form);
    updateDepositButton(form);
}

function configureCardSimulation(form) {
    const number = form.elements.cardNumber;
    const expiry = form.elements.cardExpiry;
    const cvv = form.elements.cardCvv;

    number.addEventListener("input", () => {
        const digits = number.value.replace(/\D/g, "").slice(0, 16);
        number.value = digits.replace(/(.{4})/g, "$1 ").trim();
    });
    expiry.addEventListener("input", () => {
        const digits = expiry.value.replace(/\D/g, "").slice(0, 4);
        expiry.value = digits.length > 2
            ? `${digits.slice(0, 2)}/${digits.slice(2)}`
            : digits;
    });
    cvv.addEventListener("input", () => {
        cvv.value = cvv.value.replace(/\D/g, "").slice(0, 4);
    });
}

function validatePaymentSimulation(form, method) {
    if (method !== "card") {
        return true;
    }

    const fields = [
        form.elements.cardNumber,
        form.elements.cardHolder,
        form.elements.cardExpiry,
        form.elements.cardCvv
    ];
    fields.forEach(field => field.setCustomValidity(""));

    if (form.elements.cardNumber.value.replace(/\D/g, "").length !== 16) {
        form.elements.cardNumber.setCustomValidity(
            "Ingresá los 16 dígitos de la tarjeta."
        );
    }
    if (!form.elements.cardHolder.value.trim()) {
        form.elements.cardHolder.setCustomValidity("Ingresá el nombre del titular.");
    }

    const valid = fields.every(field => field.checkValidity());
    if (!valid) {
        form.reportValidity();
    }
    return valid;
}

function createTransferSimulation(amount) {
    const reference = createPaymentReference("SUBASTAYA");
    return `
        <section class="payment-simulation-card transfer-simulation">
            <div>
                <h3>Datos para la transferencia</h3>
                <dl class="payment-details">
                    <div><dt>Titular</dt><dd>SubastaYa</dd></div>
                    <div><dt>Alias</dt><dd>subastaya.billetera <button type="button" data-copy="subastaya.billetera" data-copy-label="Alias">Copiar</button></dd></div>
                    <div><dt>CBU demostrativo</dt><dd>0000000000000000000000 <button type="button" data-copy="0000000000000000000000" data-copy-label="CBU">Copiar</button></dd></div>
                    <div><dt>Importe a transferir</dt><dd data-flow-amount>${money(amount)}</dd></div>
                    <div><dt>Concepto</dt><dd>${reference}</dd></div>
                </dl>
            </div>
            <div class="demo-qr" aria-label="QR demostrativo"><span></span><span></span><span></span><span></span><span></span><span></span><span></span><span></span><span></span><small>QR demostrativo</small></div>
            <button type="button" class="payment-cancel" data-cancel-payment>Cancelar simulación</button>
        </section>
    `;
}

function createDepositSimulation(amount) {
    return `
        <section class="payment-simulation-card">
            <h3>Instrucciones de depósito</h3>
            <dl class="payment-details">
                <div><dt>Titular</dt><dd>SubastaYa</dd></div>
                <div><dt>Importe</dt><dd data-flow-amount>${money(amount)}</dd></div>
                <div><dt>Código de depósito</dt><dd>${createPaymentReference("DEP")}</dd></div>
            </dl>
            <p>Utilizá este código como referencia para identificar tu depósito.</p>
            <small class="academic-warning">Referencia válida únicamente para esta simulación.</small>
            <button type="button" class="payment-cancel" data-cancel-payment>Cancelar simulación</button>
        </section>
    `;
}

function updatePaymentFlowAmount(form) {
    const amount = Number(form.elements.monto.value) || 0;
    form.querySelectorAll("[data-flow-amount]").forEach(element => {
        element.textContent = money(amount);
    });
}

function createPaymentReference(prefix) {
    return `${prefix}-${crypto.getRandomValues(new Uint32Array(1))[0]
        .toString()
        .slice(0, 6)
        .padStart(6, "0")}`;
}

function configureCopyButtons(container) {
    container.querySelectorAll("[data-copy]").forEach(button => {
        button.addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(button.dataset.copy);
                showToast(
                    `${button.dataset.copyLabel} copiado`,
                    "El dato demostrativo se copió al portapapeles.",
                    "success"
                );
            } catch {
                showToast(
                    "No se pudo copiar",
                    "Seleccioná y copiá el dato manualmente.",
                    "warning"
                );
            }
        });
    });
}

function cancelPaymentSimulation(form) {
    clearSensitivePaymentFields(form);
    form.elements.monto.value = "";
    form.querySelector("[data-payment-status]").textContent = "";
    form.querySelectorAll("[data-amount]").forEach(item => {
        item.classList.remove("selected");
    });
    renderPaymentFlow(form);
}

function clearSensitivePaymentFields(form) {
    ["cardNumber", "cardHolder", "cardExpiry", "cardCvv"].forEach(name => {
        if (form.elements[name]) {
            form.elements[name].value = "";
        }
    });
}

function paymentFlowMessages(method) {
    if (method === "card") {
        return {
            processing: "Procesando pago...",
            success: "Pago acreditado correctamente"
        };
    }
    if (method === "transfer") {
        return {
            processing: "Verificando transferencia...",
            success: "Transferencia acreditada correctamente"
        };
    }
    return {
        processing: "Verificando depósito...",
        success: "Depósito acreditado correctamente"
    };
}

function waitForPaymentSimulation() {
    return new Promise(resolve => window.setTimeout(resolve, 800));
}

function renderWalletBalance(balance) {
    document.getElementById("wallet-total").textContent = money(balance.total);
    document.getElementById("wallet-retained").textContent = money(balance.retenido);
    document.getElementById("wallet-available").textContent = money(balance.disponible);
}

function renderWalletMovements(container, movements) {
    if (!container) {
        return;
    }
    if (movements.length === 0) {
        container.innerHTML = createEmptyState(
            "Sin movimientos",
            "Tu actividad financiera aparecerá aquí."
        );
        return;
    }
    container.innerHTML = movements.map(movement => {
        const positive = ["Deposito", "Liberacion", "Cobro"].includes(movement.tipo);
        return `
            <article class="wallet-list-item">
                <div>
                    <strong>${escapeHtml(walletMovementLabel(movement.tipo))}</strong>
                    <p>${escapeHtml(movement.descripcion)}</p>
                    <time>${escapeHtml(formatExactDate(movement.fechaUtc))}</time>
                </div>
                <b class="${positive ? "positive" : "negative"}">
                    ${positive ? "+" : "−"}${money(movement.monto)}
                </b>
            </article>
        `;
    }).join("");
}

function renderWalletMovementError(error) {
    ["wallet-recent-movements", "wallet-all-movements"].forEach(id => {
        const container = document.getElementById(id);
        if (container) {
            container.innerHTML = createErrorState(
                "No se pudieron cargar los movimientos",
                error.message
            );
        }
    });
}

function walletMovementLabel(type) {
    return {
        Deposito: "Depósito",
        Retencion: "Retención",
        Liberacion: "Liberación",
        Pago: "Pago",
        Cobro: "Cobro"
    }[type] || type;
}

function renderRetainedFunds(response) {
    const container = document.getElementById("wallet-retained-list");
    const items = response.items || [];
    const detail = items.map(item => `
        <article class="wallet-list-item">
            <div>
                <strong>${escapeHtml(item.subasta)}</strong>
                <p>Puja ${escapeHtml(item.estado.toLowerCase())}</p>
                <time>${escapeHtml(formatExactDate(item.fechaUtc))}</time>
            </div>
            <b>${money(item.monto)}</b>
        </article>
    `);

    if (Number(response.diferencia) !== 0) {
        detail.push(`
            <article class="wallet-list-item wallet-balance-difference">
                <div>
                    <strong>Retención no vinculada a una puja abierta</strong>
                    <p>Diferencia informada por el saldo de la billetera.</p>
                </div>
                <b>${money(response.diferencia)}</b>
            </article>
        `);
    }

    container.innerHTML = detail.length
        ? `<div class="retained-total">Total retenido: <strong>${money(response.totalRetenido)}</strong></div>${detail.join("")}`
        : createEmptyState("Sin fondos retenidos", "No tenés fondos comprometidos en pujas.");
}

function renderPaymentMethods() {
    const container = document.getElementById("wallet-payment-methods");
    container.innerHTML = PAYMENT_METHODS.map(method => `
        <button
            type="button"
            class="payment-method-card ${method.id === preferredPaymentMethod ? "selected" : ""}"
            data-payment-method="${method.id}"
        >
            <i>${method.icon}</i>
            <span><strong>${method.name}</strong><small>${method.description}</small></span>
            <b>${method.id === preferredPaymentMethod ? "Preferido" : "Elegir"}</b>
        </button>
    `).join("");
    container.querySelectorAll("[data-payment-method]").forEach(button => {
        button.addEventListener("click", () => {
            preferredPaymentMethod = button.dataset.paymentMethod;
            try {
                localStorage.setItem("subastaya-wallet-method", preferredPaymentMethod);
            } catch {
                // La preferencia sigue disponible durante esta sesión.
            }
            document.querySelectorAll("[data-deposit-form] select[name='metodo']")
                .forEach(select => {
                    select.value = preferredPaymentMethod;
                    renderPaymentFlow(select.form);
                    updateDepositButton(select.form);
                });
            renderPaymentMethods();
        });
    });
}


/* =========================
   CONTROLES VISUALES EXISTENTES
   ========================= */

/* =========================
   AUTENTICACIÓN Y SESIÓN
   ========================= */

function loadUserSession() {
    try {
        const storedSession = localStorage.getItem(SESSION_STORAGE_KEY);

        if (!storedSession) {
            return null;
        }

        const session = JSON.parse(storedSession);

        if (!session?.userId) {
            return null;
        }

        return session;
    } catch (error) {
        console.error("No se pudo recuperar la sesión.", error);
        localStorage.removeItem(SESSION_STORAGE_KEY);
        return null;
    }
}

function saveUserSession(user) {
    currentSession = {
        userId: user.userId,
        nombre: user.nombre,
        email: user.email
    };

    localStorage.setItem(
        SESSION_STORAGE_KEY,
        JSON.stringify(currentSession)
    );

    updateAuthenticationUI();
}

function clearUserSession() {
    currentSession = null;
    myPublications = [];

    localStorage.removeItem(SESSION_STORAGE_KEY);

    updateAuthenticationUI();

    history.replaceState(null, "", "#catalog");
    showView("catalog", false);

    void leaveLiveRoom();
}

function updateAuthenticationUI() {
    const isLoggedIn = Boolean(currentSession?.userId);

    const userName =
        document.getElementById("active-user-name");

    const guestMenuItems =
        document.querySelectorAll(
            ".guest-menu-item, #guest-menu"
        );

    const loggedMenuItems =
        document.querySelectorAll(
            ".logged-menu-item, #logged-menu"
        );

    const sellerName =
        document.getElementById("auction-seller-name");

    const dropdownName =
        document.getElementById("dropdown-user-name");

    const dropdownEmail =
        document.getElementById("dropdown-user-email");


    if (userName) {
        userName.textContent =
            isLoggedIn
                ? currentSession.nombre
                : "Login";
    }


    guestMenuItems.forEach(item => {
        item.hidden = isLoggedIn;
    });


    loggedMenuItems.forEach(item => {
        item.hidden = !isLoggedIn;
    });


    document
        .querySelectorAll(".auth-only")
        .forEach(element => {
            element.hidden = !isLoggedIn;
        });


    if (sellerName) {
        sellerName.textContent =
            isLoggedIn
                ? currentSession.nombre
                : "";
    }

    if (dropdownName) {
        dropdownName.textContent = isLoggedIn
            ? currentSession.nombre
            : "";
    }

    if (dropdownEmail) {
        dropdownEmail.textContent = isLoggedIn
            ? currentSession.email
            : "";
    }
}

function configureAuthentication() {
    updateAuthenticationUI();

    const loginForm =
        document.getElementById("login-form");

    const registerForm =
        document.getElementById("register-form");

    const logoutButton =
        document.getElementById("logout-button");

    const editProfileButton =
        document.getElementById("edit-profile-button");

    const profileForm =
        document.getElementById("profile-form");


    loginForm?.addEventListener(
        "submit",
        loginUser
    );


    registerForm?.addEventListener(
        "submit",
        registerUser
    );

    editProfileButton?.addEventListener(
        "click",
        openProfileEditor
    );

    profileForm?.addEventListener(
        "submit",
        updateUserProfile
    );


    logoutButton?.addEventListener(
        "click",
        () => {

            const dropdownButton =
                document.getElementById(
                    "auth-menu-button"
                );

            if (dropdownButton) {
                bootstrap.Dropdown
                    .getOrCreateInstance(
                        dropdownButton
                    )
                    .hide();
            }

            clearUserSession();

            showToast(
                "Sesión cerrada",
                "Cerraste sesión correctamente.",
                "success"
            );
        }
    );
}

async function refreshCurrentUserProfile() {
    try {
        const response = await fetch(
            `${API_BASE_URL}/api/users/me`,
            getAuthenticatedRequestOptions({ cache: "no-store" })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        saveUserSession(await response.json());
    } catch (error) {
        console.warn("No se pudo sincronizar el perfil visible.", error.message);
    }
}

function openProfileEditor() {
    if (!currentSession?.userId) {
        return;
    }

    const dropdownButton = document.getElementById("auth-menu-button");
    bootstrap.Dropdown.getOrCreateInstance(dropdownButton).hide();

    const form = document.getElementById("profile-form");
    form.elements.nombre.value = currentSession.nombre || "";
    form.elements.email.value = currentSession.email || "";
    setAuthFeedback("profile-feedback", "");

    bootstrap.Modal.getOrCreateInstance(
        document.getElementById("profile-modal")
    ).show();
}

async function updateUserProfile(event) {
    event.preventDefault();

    const form = event.currentTarget;
    const submitButton = document.getElementById("profile-submit");
    const nombre = form.elements.nombre.value.trim();
    const email = form.elements.email.value.trim();

    if (!nombre) {
        setAuthFeedback(
            "profile-feedback",
            "El nombre es obligatorio.",
            "error"
        );
        return;
    }

    if (!form.reportValidity()) {
        return;
    }

    submitButton.disabled = true;
    submitButton.textContent = "Guardando…";
    setAuthFeedback("profile-feedback", "");

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/users/me`,
            getAuthenticatedRequestOptions({
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ nombre, email })
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        saveUserSession(await response.json());
        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("profile-modal")
        ).hide();
        showToast(
            "Perfil actualizado correctamente",
            "Tus datos visibles ya están actualizados.",
            "success"
        );
    } catch (error) {
        setAuthFeedback(
            "profile-feedback",
            error.message,
            "error"
        );
    } finally {
        submitButton.disabled = false;
        submitButton.textContent = "Guardar cambios";
    }
}

async function loginUser(event) {
    event.preventDefault();

    const form = event.currentTarget;

    const submitButton =
        document.getElementById(
            "login-submit"
        );

    const email =
        form.elements.email.value.trim();

    const password =
        form.elements.password.value;


    setAuthFeedback(
        "login-feedback",
        ""
    );


    submitButton.disabled = true;
    submitButton.textContent =
        "Ingresando…";


    try {

        const response = await fetch(
            `${API_BASE_URL}/api/auth/login`,
            {
                method: "POST",

                headers: {
                    "Content-Type":
                        "application/json"
                },

                credentials: "include",

                body: JSON.stringify({
                    email,
                    password
                })
            }
        );


        if (!response.ok) {
            throw new Error(
                await readApiError(response)
            );
        }


        const user =
            await response.json();


        saveUserSession(user);


        form.reset();


        bootstrap.Modal
            .getOrCreateInstance(
                document.getElementById(
                    "login-modal"
                )
            )
            .hide();


        showToast(
            "Bienvenido",
            `Ingresaste como ${user.nombre}.`,
            "success"
        );

    } catch (error) {

        setAuthFeedback(
            "login-feedback",
            error.message,
            "error"
        );

    } finally {

        submitButton.disabled = false;
        submitButton.textContent =
            "Ingresar";
    }
}

async function registerUser(event) {
    event.preventDefault();

    const form = event.currentTarget;

    const submitButton =
        document.getElementById(
            "register-submit"
        );


    const nombre =
        form.elements.nombre.value.trim();

    const email =
        form.elements.email.value.trim();

    const password =
        form.elements.password.value;

    const confirmPassword =
        form.elements.confirmPassword.value;


    setAuthFeedback(
        "register-feedback",
        ""
    );


    if (password !== confirmPassword) {

        setAuthFeedback(
            "register-feedback",
            "Las contraseñas no coinciden.",
            "error"
        );

        return;
    }


    if (password.length < 8) {

        setAuthFeedback(
            "register-feedback",
            "La contraseña debe tener al menos 8 caracteres.",
            "error"
        );

        return;
    }


    submitButton.disabled = true;
    submitButton.textContent =
        "Creando cuenta…";


    try {

        const response = await fetch(
            `${API_BASE_URL}/api/auth/register`,
            {
                method: "POST",

                headers: {
                    "Content-Type":
                        "application/json"
                },

                credentials: "include",

                body: JSON.stringify({
                    nombre,
                    email,
                    password
                })
            }
        );


        if (!response.ok) {
            throw new Error(
                await readApiError(response)
            );
        }


        await response.json();

        form.reset();


        bootstrap.Modal
            .getOrCreateInstance(
                document.getElementById(
                    "register-modal"
                )
            )
            .hide();


        const loginForm =
            document.getElementById(
                "login-form"
            );

        if (loginForm) {
            loginForm.elements.email.value =
                email;
        }


        showToast(
            "Usuario creado",
            "La cuenta fue creada. Ahora podés ingresar.",
            "success"
        );


        window.setTimeout(
            () => {

                bootstrap.Modal
                    .getOrCreateInstance(
                        document.getElementById(
                            "login-modal"
                        )
                    )
                    .show();

            },
            300
        );

    } catch (error) {

        setAuthFeedback(
            "register-feedback",
            error.message,
            "error"
        );

    } finally {

        submitButton.disabled = false;
        submitButton.textContent =
            "Crear cuenta";
    }
}

function setAuthFeedback(
    elementId,
    message,
    type = ""
) {
    const feedback =
        document.getElementById(elementId);

    if (!feedback) {
        return;
    }

    feedback.textContent = message;

    feedback.classList.remove(
        "error",
        "success"
    );

    if (type) {
        feedback.classList.add(type);
    }
}
async function loadCategories() {
    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/categories`,
            {
                cache: "no-store",
                credentials: "include"
            }
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        availableCategories = normalizeCategories(await response.json());
        renderCategoryControls();
    } catch (error) {
        console.error("No se pudieron cargar las categorías.", error);
        showToast(
            "Categorías no disponibles",
            error.message,
            "warning"
        );
    }
}

function normalizeCategories(categories) {
    const unique = new Map();

    categories.forEach(value => {
        const category = String(value ?? "").trim();
        const key = category.toLocaleLowerCase("es");

        if (category && !unique.has(key)) {
            unique.set(key, category);
        }
    });

    return [...unique.values()].sort((first, second) => {
        return first.localeCompare(second, "es", { sensitivity: "base" });
    });
}

function configureCategoryControls() {
    document.querySelectorAll("[data-category-select]").forEach(select => {
        select.addEventListener("change", () => {
            synchronizeNewCategoryField(select.form);
        });

        select.form.elements.nuevaCategoria.addEventListener("input", event => {
            event.currentTarget.setCustomValidity("");
        });
    });
}

function renderCategoryControls() {
    const filter = document.getElementById("catalog-category-filter");
    const selectedFilter = filter.value;

    replaceCategoryOptions(filter, "Todas", false);
    filter.value = availableCategories.includes(selectedFilter)
        ? selectedFilter
        : "";

    document.querySelectorAll("[data-category-select]").forEach(select => {
        const selectedCategory = select.value;
        replaceCategoryOptions(
            select,
            select.dataset.placeholder,
            true,
            selectedCategory
        );
        synchronizeNewCategoryField(select.form);
    });
}

function replaceCategoryOptions(
    select,
    placeholder,
    includeNewCategory,
    extraCategory = ""
) {
    const categories = [...availableCategories];
    const normalizedExtra = String(extraCategory ?? "").trim();

    if (normalizedExtra &&
        normalizedExtra !== NEW_CATEGORY_VALUE &&
        !categories.some(category => {
            return category.localeCompare(
                normalizedExtra,
                "es",
                { sensitivity: "base" }
            ) === 0;
        })) {
        categories.push(normalizedExtra);
        categories.sort((first, second) => {
            return first.localeCompare(second, "es", { sensitivity: "base" });
        });
    }

    select.replaceChildren(new Option(placeholder, ""));
    categories.forEach(category => {
        select.add(new Option(category, category));
    });

    if (includeNewCategory) {
        select.add(new Option("+ Nueva categoría", NEW_CATEGORY_VALUE));
    }

    select.value = normalizedExtra;
}

function synchronizeNewCategoryField(form) {
    const select = form.elements.categoria;
    const field = form.querySelector("[data-new-category-field]");
    const input = form.elements.nuevaCategoria;
    const creatingCategory = select.value === NEW_CATEGORY_VALUE;

    field.hidden = !creatingCategory;
    input.required = creatingCategory;

    if (!creatingCategory) {
        input.value = "";
        input.setCustomValidity("");
    }
}

function getCategoryValue(form) {
    const select = form.elements.categoria;
    const input = form.elements.nuevaCategoria;

    if (select.value !== NEW_CATEGORY_VALUE) {
        return select.value.trim();
    }

    const category = input.value.trim();
    input.setCustomValidity(
        category ? "" : "Ingresá el nombre de la nueva categoría."
    );
    return category;
}


/* =========================
   CREAR SUBASTA
   ========================= */

function configureAuctionCreation() {
    const openButton = document.getElementById("open-auction-form");
    const form = document.getElementById("auction-form");

    document.getElementById("auction-seller-name").textContent =
        currentSession?.nombre ?? "";
    setAuctionFormDates(form);

    openButton.addEventListener("click", () => {
        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("auction-form-modal")
        ).show();
    });

    form.addEventListener("submit", createAuction);
}

function setAuctionFormDates(form) {
    const now = new Date();
    const end = new Date(now.getTime() + 60 * 60 * 1000);

    form.elements.fechaInicioUtc.value = toLocalDateTimeInput(now);
    form.elements.fechaFinUtc.value = toLocalDateTimeInput(end);
}

async function createAuction(event) {
    event.preventDefault();

    const form = event.currentTarget;
    const submitButton = document.getElementById("create-auction-submit");
    const startDate = new Date(form.elements.fechaInicioUtc.value);
    const endDate = new Date(form.elements.fechaFinUtc.value);
    const category = getCategoryValue(form);

    form.elements.fechaFinUtc.setCustomValidity(
        endDate > startDate
            ? ""
            : "La finalización debe ser posterior al inicio."
    );

    if (!form.reportValidity()) {
        return;
    }

    const request = {
        titulo: form.elements.titulo.value.trim(),
        descripcion: form.elements.descripcion.value.trim(),
        imagenUrl: form.elements.imagenUrl.value.trim() || null,
        categoria: category,
        precioInicial: Number(form.elements.precioInicial.value),
        incrementoMinimo: Number(form.elements.incrementoMinimo.value),
        fechaInicioUtc: startDate.toISOString(),
        fechaFinUtc: endDate.toISOString()
    };

    submitButton.disabled = true;
    submitButton.textContent = "Publicando…";

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions`,
            getAuthenticatedRequestOptions({
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(request)
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        const auction = await response.json();

        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("auction-form-modal")
        ).hide();
        form.reset();
        synchronizeNewCategoryField(form);
        setAuctionFormDates(form);
        showToast(
            "Subasta publicada",
            `“${auction.titulo}” ya forma parte de tus publicaciones.`,
            "success"
        );
        await loadCategories();
        await loadAuctions();

        if (location.hash !== "#activities") {
            history.pushState(null, "", "#activities");
        }

        await handleRoute(false);
    } catch (error) {
        showToast("No se pudo publicar", error.message, "danger");
    } finally {
        submitButton.disabled = false;
        submitButton.textContent = "Publicar subasta";
    }
}


/* =========================
   MIS ACTIVIDADES
   ========================= */

function configureActivities() {
    document.querySelectorAll(".side-nav a[href^='#']").forEach(link => {
        link.addEventListener("click", event => {
            const section = document.querySelector(link.getAttribute("href"));

            if (!section) {
                return;
            }

            event.preventDefault();
            link.closest(".side-nav").querySelectorAll("a").forEach(item => {
                item.classList.toggle("active", item === link);
            });
            section.scrollIntoView({ behavior: "smooth", block: "start" });
        });
    });

    document.querySelectorAll("[data-publication-state]").forEach(button => {
        button.addEventListener("click", () => {
            publicationFilter = button.dataset.publicationState;
            renderMyPublications();
        });
    });

    document.querySelectorAll("[data-purchase-state]").forEach(button => {
        button.addEventListener("click", () => {
            purchaseFilter = button.dataset.purchaseState;
            renderMyBidActivities();
        });
    });

    document.getElementById("edit-auction-form").addEventListener(
        "submit",
        updatePublication
    );
    document.getElementById("confirm-delete-auction").addEventListener(
        "click",
        deletePublication
    );
}


/* =========================
   MIS COMPRAS / PUJAS
   ========================= */

async function loadMyBidActivities() {
    const body = document.getElementById("purchases-table-body");
    const feedback = document.getElementById("purchases-feedback");

    body.innerHTML = "";
    feedback.hidden = false;
    feedback.innerHTML = `
        <i>◴</i>
        <strong>Cargando compras y pujas</strong>
    `;

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/my-bids`,
            getAuthenticatedRequestOptions({ cache: "no-store" })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        myBidActivities = await response.json();
        renderMyBidActivities();

        try {
            await synchronizeActivityAuctionGroups();
        } catch (error) {
            showToast(
                "Tiempo real no disponible",
                error.message,
                "warning"
            );
        }
    } catch (error) {
        feedback.hidden = false;
        feedback.innerHTML = `
            <i>!</i>
            <strong>No se pudieron cargar tus compras y pujas</strong>
            <p>${escapeHtml(error.message)}</p>
        `;
    }
}

function renderMyBidActivities() {
    const body = document.getElementById("purchases-table-body");
    const feedback = document.getElementById("purchases-feedback");
    const activities = myBidActivities.filter(activity => {
        return activity.resultado === purchaseFilter;
    });

    updateBidActivityCounts();
    body.innerHTML = "";

    if (activities.length === 0) {
        const messages = {
            EnCurso: [
                "No tenés pujas en curso.",
                "Las subastas abiertas en las que participes aparecerán aquí."
            ],
            Ganada: [
                "Todavía no ganaste ninguna subasta.",
                "Tus compras adjudicadas aparecerán aquí."
            ],
            Perdida: [
                "No tenés subastas perdidas.",
                "Las subastas cerradas que no ganaste aparecerán aquí."
            ]
        };
        const [title, description] = messages[purchaseFilter];

        feedback.hidden = false;
        feedback.innerHTML = `
            <i>⌁</i>
            <strong>${escapeHtml(title)}</strong>
            <p>${escapeHtml(description)}</p>
        `;
        stopActivityCountdown();
        return;
    }

    feedback.hidden = true;
    activities.forEach(activity => {
        const row = document.createElement("tr");
        const state = auctionStateNames[activity.estado] ?? activity.estado;
        const isOpen = activity.resultado === "EnCurso";

        row.innerHTML = `
            <td>
                <div class="activity-auction">
                    <img
                        src="${escapeHtml(activity.imagenUrl || DEFAULT_AUCTION_IMAGE)}"
                        alt="${escapeHtml(activity.titulo)}"
                    >
                    <div>
                        <strong>${escapeHtml(activity.titulo)}</strong>
                        <small>${escapeHtml(activity.categoria)}</small>
                    </div>
                </div>
            </td>
            <td>${money(activity.miOferta)}</td>
            <td>${money(activity.ofertaActual)}</td>
            <td>
                <span class="activity-status activity-status-${activity.resultado.toLowerCase()}">
                    ${escapeHtml(state)}
                </span>
            </td>
            <td
                ${isOpen
                    ? `data-activity-id="${activity.subastaId}" data-activity-end="${escapeHtml(activity.fechaFinUtc)}"`
                    : ""}
            >
                ${isOpen
                    ? formatRemainingTime(getRemainingMilliseconds(activity.fechaFinUtc))
                    : state}
            </td>
            <td>
                <button type="button" class="activity-view-action">
                    Ver subasta
                </button>
            </td>
        `;
        configureImageFallback(row.querySelector("img"));
        row.querySelector(".activity-view-action").addEventListener(
            "click",
            () => viewBidActivity(activity)
        );
        body.appendChild(row);
    });

    startActivityCountdown();
}

function updateBidActivityCounts() {
    document.getElementById("purchase-count-active").textContent =
        myBidActivities.filter(item => item.resultado === "EnCurso").length;
    document.getElementById("purchase-count-won").textContent =
        myBidActivities.filter(item => item.resultado === "Ganada").length;
    document.getElementById("purchase-count-lost").textContent =
        myBidActivities.filter(item => item.resultado === "Perdida").length;
}

function startActivityCountdown() {
    stopActivityCountdown();
    updateActivityCountdowns();
    activityCountdownTimer = window.setInterval(updateActivityCountdowns, 1000);
}

function stopActivityCountdown() {
    window.clearInterval(activityCountdownTimer);
    activityCountdownTimer = null;
}

function updateActivityCountdowns() {
    let reachedEnd = false;

    document.querySelectorAll("[data-activity-end]").forEach(element => {
        const remaining = getRemainingMilliseconds(element.dataset.activityEnd);
        const expirationKey =
            `${element.dataset.activityId}|${element.dataset.activityEnd}`;
        element.textContent = remaining > 0
            ? formatRemainingTime(remaining)
            : "Finalizando…";

        if (
            remaining <= 0 &&
            !activityExpirationRefreshes.has(expirationKey)
        ) {
            activityExpirationRefreshes.add(expirationKey);
            reachedEnd = true;
        }
    });

    if (reachedEnd) {
        scheduleBidActivitiesRefresh();
    }
}

function scheduleBidActivitiesRefresh(subastaId = null) {
    if (getViewFromHash() !== "activities") {
        return;
    }

    if (subastaId && !myBidActivities.some(item => item.subastaId === subastaId)) {
        return;
    }

    window.clearTimeout(activityRefreshTimer);
    activityRefreshTimer = window.setTimeout(() => {
        activityRefreshTimer = null;
        void loadMyBidActivities();
    }, 250);
}

async function synchronizeActivityAuctionGroups() {
    if (!window.signalR || getViewFromHash() !== "activities") {
        return;
    }

    ensureAuctionHubConnection();

    if (hubConnection.state === signalR.HubConnectionState.Disconnected) {
        await hubConnection.start();
    }

    const desiredIds = new Set(myBidActivities.map(item => item.subastaId));

    for (const auctionId of [...activityJoinedAuctionIds]) {
        if (!desiredIds.has(auctionId)) {
            await hubConnection.invoke("LeaveAuction", auctionId);
            activityJoinedAuctionIds.delete(auctionId);
        }
    }

    for (const auctionId of desiredIds) {
        if (!activityJoinedAuctionIds.has(auctionId)) {
            await hubConnection.invoke("JoinAuction", auctionId);
            activityJoinedAuctionIds.add(auctionId);
        }
    }
}

async function rejoinActivityAuctionGroups() {
    if (getViewFromHash() !== "activities") {
        return;
    }

    for (const auctionId of activityJoinedAuctionIds) {
        await hubConnection.invoke("JoinAuction", auctionId);
    }
}

async function leaveActivityAuctionGroups() {
    stopActivityCountdown();
    window.clearTimeout(activityRefreshTimer);
    activityRefreshTimer = null;

    if (hubConnection?.state === window.signalR?.HubConnectionState.Connected) {
        for (const auctionId of activityJoinedAuctionIds) {
            try {
                await hubConnection.invoke("LeaveAuction", auctionId);
            } catch {
                break;
            }
        }
    }

    activityJoinedAuctionIds.clear();
}

async function viewBidActivity(activity) {
    await leaveActivityAuctionGroups();
    await viewPublication({
        id: activity.subastaId,
        titulo: activity.titulo,
        descripcion: activity.descripcion,
        imagenUrl: activity.imagenUrl,
        categoria: activity.categoria,
        precioActual: activity.ofertaActual,
        incrementoMinimo: activity.incrementoMinimo,
        fechaFinUtc: activity.fechaFinUtc,
        estado: activity.estado
    });
}


/* =========================
   MIS PUBLICACIONES
   ========================= */

async function loadMyPublications() {
    const container = document.getElementById("publications-container");

    container.innerHTML = createLoadingState("Cargando publicaciones");

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/mine`,
            getAuthenticatedRequestOptions({
                cache: "no-store"
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        myPublications = await response.json();
        renderMyPublications();
    } catch (error) {
        container.innerHTML = createErrorState(
            "No se pudieron cargar tus publicaciones",
            error.message
        );
    }
}

function renderMyPublications() {
    const container = document.getElementById("publications-container");
    const publications = myPublications.filter(auction => {
        const status = auctionStateNames[auction.estado] ?? auction.estado;
        return publicationFilter === "all" || status === publicationFilter;
    });

    updatePublicationCounts();
    container.innerHTML = "";

    if (publications.length === 0) {
        container.innerHTML = createEmptyState(
            "Sin publicaciones para mostrar",
            publicationFilter === "all"
                ? "Las subastas que publiques aparecerán aquí."
                : "No hay publicaciones con este estado."
        );
        return;
    }

    publications.forEach(auction => {
        const status = auctionStateNames[auction.estado] ?? auction.estado;
        const card = document.createElement("article");

        card.className = "publication-card";
        card.innerHTML = `
            <img
                src="${escapeHtml(auction.imagenUrl || DEFAULT_AUCTION_IMAGE)}"
                alt="${escapeHtml(auction.titulo)}"
            >
            <div class="publication-card-body">
                <div class="publication-card-heading">
                    <h3>${escapeHtml(auction.titulo)}</h3>
                    <span class="publication-status">${escapeHtml(status)}</span>
                </div>
                <dl>
                    <div>
                        <dt>Precio inicial</dt>
                        <dd>${money(auction.precioInicial)}</dd>
                    </div>
                    <div>
                        <dt>Precio actual</dt>
                        <dd>${money(auction.precioActual)}</dd>
                    </div>
                    <div class="publication-end-date">
                        <dt>Finaliza</dt>
                        <dd>${formatExactDate(auction.fechaFinUtc)}</dd>
                    </div>
                </dl>
                <div class="publication-actions">
                    <button type="button" data-publication-action="view">Ver</button>
                    <button type="button" data-publication-action="edit">Editar</button>
                    <button
                        type="button"
                        class="danger-outline-action"
                        data-publication-action="delete"
                    >
                        Eliminar
                    </button>
                </div>
            </div>
        `;
        configureImageFallback(card.querySelector("img"));
        card.querySelector("[data-publication-action='view']")
            .addEventListener("click", () => viewPublication(auction));
        card.querySelector("[data-publication-action='edit']")
            .addEventListener("click", () => openEditPublication(auction));
        card.querySelector("[data-publication-action='delete']")
            .addEventListener("click", () => openDeletePublication(auction));
        container.appendChild(card);
    });
}

async function viewPublication(publication) {
    const auction = normalizeAuction(publication);
    const existingIndex = catalogAuctions.findIndex(item => item.id === auction.id);

    if (existingIndex >= 0) {
        catalogAuctions[existingIndex] = auction;
    } else {
        catalogAuctions.push(auction);
    }

    history.pushState(null, "", `#auction/${auction.id}`);
    await openLiveRoom(auction, true);
}

function openEditPublication(auction) {
    const form = document.getElementById("edit-auction-form");

    form.elements.id.value = auction.id;
    form.elements.titulo.value = auction.titulo;
    form.elements.descripcion.value = auction.descripcion;
    replaceCategoryOptions(
        form.elements.categoria,
        form.elements.categoria.dataset.placeholder,
        true,
        auction.categoria
    );
    synchronizeNewCategoryField(form);
    form.elements.imagenUrl.value = auction.imagenUrl ?? "";
    form.elements.precioInicial.value = money(auction.precioInicial);
    form.elements.incrementoMinimo.value = money(auction.incrementoMinimo);
    form.elements.fechaFinUtc.value = toLocalDateTimeInput(auction.fechaFinUtc);
    form.elements.fechaFinUtc.setCustomValidity("");

    bootstrap.Modal.getOrCreateInstance(
        document.getElementById("edit-auction-modal")
    ).show();
}

async function updatePublication(event) {
    event.preventDefault();

    const form = event.currentTarget;
    const submitButton = document.getElementById("edit-auction-submit");
    const endDate = new Date(form.elements.fechaFinUtc.value);
    const category = getCategoryValue(form);

    form.elements.fechaFinUtc.setCustomValidity(
        endDate > new Date()
            ? ""
            : "La fecha de finalización debe ser futura."
    );

    if (!form.reportValidity()) {
        return;
    }

    const request = {
        titulo: form.elements.titulo.value.trim(),
        descripcion: form.elements.descripcion.value.trim(),
        imagenUrl: form.elements.imagenUrl.value.trim() || null,
        categoria: category,
        fechaFinUtc: endDate.toISOString()
    };

    submitButton.disabled = true;
    submitButton.textContent = "Guardando…";

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/${form.elements.id.value}`,
            getAuthenticatedRequestOptions({
                method: "PUT",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(request)
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("edit-auction-modal")
        ).hide();
        await loadCategories();
        await loadMyPublications();
        await loadAuctions();
        showToast(
            "Publicación actualizada",
            "Los cambios se guardaron correctamente.",
            "success"
        );
    } catch (error) {
        showToast("No se pudo editar", error.message, "danger");
    } finally {
        submitButton.disabled = false;
        submitButton.textContent = "Guardar cambios";
    }
}

function openDeletePublication(auction) {
    publicationPendingDeletion = auction;
    document.getElementById("delete-auction-name").textContent = auction.titulo;
    bootstrap.Modal.getOrCreateInstance(
        document.getElementById("delete-auction-modal")
    ).show();
}

async function deletePublication() {
    if (!publicationPendingDeletion) {
        return;
    }

    const button = document.getElementById("confirm-delete-auction");
    const auction = publicationPendingDeletion;

    button.disabled = true;
    button.textContent = "Eliminando…";

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/${auction.id}`,
            getAuthenticatedRequestOptions({
                method: "DELETE"
            })
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        bootstrap.Modal.getOrCreateInstance(
            document.getElementById("delete-auction-modal")
        ).hide();
        publicationPendingDeletion = null;
        await loadCategories();
        await loadMyPublications();
        await loadAuctions();
        showToast(
            "Publicación eliminada",
            "Publicación eliminada correctamente.",
            "success"
        );
    } catch (error) {
        showToast("No se pudo eliminar", error.message, "danger");
    } finally {
        button.disabled = false;
        button.textContent = "Eliminar publicación";
    }
}

function updatePublicationCounts() {
    const statuses = myPublications.map(auction => {
        return auctionStateNames[auction.estado] ?? auction.estado;
    });

    document.getElementById("publication-count-all").textContent =
        myPublications.length;
    document.getElementById("publication-count-active").textContent =
        statuses.filter(status => status === "Activa").length;
    document.getElementById("publication-count-finished").textContent =
        statuses.filter(status => status === "Finalizada").length;
    document.getElementById("publication-count-deserted").textContent =
        statuses.filter(status => status === "Desierta").length;
}

function configureVisualControls() {
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

function toLocalDateTimeInput(value) {
    const date = new Date(value);
    const localDate = new Date(
        date.getTime() - date.getTimezoneOffset() * 60 * 1000
    );

    return localDate.toISOString().slice(0, 16);
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
