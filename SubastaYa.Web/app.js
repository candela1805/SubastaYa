const API_BASE_URL = "http://localhost:5000";
const DEFAULT_AUCTION_IMAGE = createPlaceholderImage();

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
let joinedAuctionId = null;
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
                cache: "no-store"
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
    bidHistory = [];

    showView("live", scroll);
    renderLiveAuction();
    startCountdown();

    await Promise.all([
        loadBidHistory(),
        connectToAuctionHub()
    ]);
}

function renderLiveAuction() {
    const image = document.getElementById("live-image");

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

    updateBidMinimum();
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
    const active =
        activeAuction.status === "Activa" &&
        getRemainingMilliseconds(activeAuction.endDateUtc) > 0;
    const status = document.getElementById("live-status");

    status.textContent = active ? "Activa" : activeAuction.status;
    status.classList.toggle("ending", !active);
    document.getElementById("bid-amount").disabled = !active;
    document.getElementById("place-bid-button").disabled = !active;
}

function updateBidMinimum() {
    const minimum = activeAuction.currentBid + activeAuction.minimumIncrement;
    const input = document.getElementById("bid-amount");

    input.min = minimum;
    input.value = minimum;
    document.getElementById("bid-minimum-help").textContent =
        `Oferta mínima permitida: ${money(minimum)}`;
}

async function loadBidHistory() {
    const container = document.getElementById("bid-history");

    container.innerHTML = createLoadingState("Cargando historial");

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/${activeAuction.id}/bids`,
            {
                cache: "no-store"
            }
        );

        if (!response.ok) {
            throw new Error(await readApiError(response));
        }

        bidHistory = await response.json();
        renderBidHistory();
    } catch (error) {
        container.innerHTML = createErrorState(
            "No se pudo cargar el historial",
            error.message
        );
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

    const button = document.getElementById("place-bid-button");
    const amount = Number(document.getElementById("bid-amount").value);

    button.disabled = true;
    button.textContent = "Procesando…";

    try {
        const response = await fetch(
            `${API_BASE_URL}/api/auctions/${activeAuction.id}/bids`,
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    monto: amount
                })
            }
        );

        if (!response.ok) {
            const message = await readApiError(response);
            showBidError(response.status, message);
            return;
        }

        const bid = await response.json();
        applyBid(bid);
        showToast("Puja confirmada", `Tu oferta de ${money(bid.monto)} fue registrada.`, "success");
    } catch (error) {
        showToast("Error inesperado", error.message, "danger");
    } finally {
        button.textContent = "⚒  Realizar puja";
        updateAuctionAvailability();
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

    const catalogAuction = catalogAuctions.find(auction => {
        return auction.id === bid.subastaId;
    });

    if (catalogAuction) {
        catalogAuction.currentBid = bid.precioActual;
        catalogAuction.endDateUtc = bid.fechaFinUtc;

        const price = document.querySelector(
            `[data-auction-card-id="${bid.subastaId}"] [data-auction-price]`
        );

        if (price) {
            price.textContent = money(bid.precioActual);
        }
    }

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


/* =========================
   SIGNALR
   ========================= */

async function connectToAuctionHub() {
    const connectionStatus = document.getElementById("connection-status");

    if (!window.signalR) {
        connectionStatus.textContent = "Sin conexión en vivo";
        return;
    }

    if (!hubConnection) {
        hubConnection = new signalR.HubConnectionBuilder()
            .withUrl(`${API_BASE_URL}/hubs/auctions`)
            .withAutomaticReconnect()
            .build();

        hubConnection.on("BidPlaced", applyBid);
        hubConnection.on("AuctionStateChanged", applyAuctionState);
        hubConnection.onreconnecting(() => {
            connectionStatus.textContent = "Reconectando…";
        });
        hubConnection.onreconnected(async () => {
            connectionStatus.textContent = "Conectado";

            if (activeAuction) {
                await hubConnection.invoke("JoinAuction", activeAuction.id);
                joinedAuctionId = activeAuction.id;
                await loadBidHistory();
            }
        });
        hubConnection.onclose(() => {
            connectionStatus.textContent = "Desconectado";
        });
    }

    try {
        if (hubConnection.state === signalR.HubConnectionState.Disconnected) {
            await hubConnection.start();
        }

        if (joinedAuctionId && joinedAuctionId !== activeAuction.id) {
            await hubConnection.invoke("LeaveAuction", joinedAuctionId);
        }

        if (joinedAuctionId !== activeAuction.id) {
            await hubConnection.invoke("JoinAuction", activeAuction.id);
            joinedAuctionId = activeAuction.id;
        }

        connectionStatus.textContent = "Conectado";
    } catch (error) {
        connectionStatus.textContent = "Sin conexión en vivo";
        showToast("Tiempo real no disponible", error.message, "warning");
    }
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

    if (
        joinedAuctionId &&
        hubConnection?.state === signalR.HubConnectionState.Connected
    ) {
        try {
            await hubConnection.invoke("LeaveAuction", joinedAuctionId);
        } catch {
            // La reconexión automática resolverá la membresía del grupo.
        }
    }

    joinedAuctionId = null;
    activeAuction = null;
    bidHistory = [];
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
                cache: "no-store"
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
    return new Intl.NumberFormat("es-AR", {
        style: "currency",
        currency: "ARS",
        maximumFractionDigits: 2
    }).format(value);
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
