// app.js - Lógica Frontend Vanilla JS

document.addEventListener("DOMContentLoaded", () => {
    // Inicializar la aplicación cargando el catálogo
    fetchAuctions();
});

/**
 * Módulo 1: Catálogo y Exploración de Subastas
 * Consume la API RESTful de C# de manera asíncrona.
 */
async function fetchAuctions() {
    // URL base de la API (Asegúrate de que coincida con tu puerto de .NET Core)
    const API_URL = "http://localhost:5000/api/auctions";
    const container = document.getElementById("auctions-container");
    const spinner = document.getElementById("loading-spinner");

    try {
        const response = await fetch(API_URL);
        if (!response.ok) {
            throw new Error(`Error HTTP: ${response.status}`);
        }
        const auctions = await response.json();

        // Ocultar el spinner
        spinner.style.display = "none";

        // Renderizar las tarjetas en el DOM
        renderAuctions(auctions, container);
    } catch (error) {
        console.error("Fallo al cargar las subastas:", error);
        spinner.innerHTML = `
            <div class="alert alert-danger mx-auto mt-3" style="max-width: 500px;">
                <h5 class="alert-heading">Error de Conexión</h5>
                <p>No se pudo conectar con la API en <strong>${API_URL}</strong>.</p>
                <hr>
                <p class="mb-0 small">Verificá que tu backend en C# esté corriendo y tenga configurados los CORS para permitir peticiones desde el navegador.</p>
            </div>
        `;
    }
}

/**
 * Dibuja las tarjetas informativas en el HTML.
 */
function renderAuctions(auctions, container) {
    container.replaceChildren();

    if (!Array.isArray(auctions) || auctions.length === 0) {
        const emptyMessage = document.createElement("div");
        emptyMessage.className = "alert alert-info text-center";
        emptyMessage.textContent = "No hay subastas activas en este momento.";
        container.appendChild(emptyMessage);
        return;
    }

    auctions.forEach(auction => {
        // Lógica visual para subastas por terminar
        const remainingMilliseconds = getRemainingMilliseconds(auction.endTime);
        const isEndingSoon = remainingMilliseconds > 0 && remainingMilliseconds <= 60_000;
        const badgeColor = isEndingSoon ? "text-danger" : "text-primary";
        const badgeIcon = isEndingSoon ? "🔥 Por terminar" : "✅ Activa";

        const card = document.createElement("div");
        card.className = "col-12 col-md-6 col-lg-4";
        card.innerHTML = `
            <div class="card shadow-sm auction-card h-100">
                <div class="auction-img-wrapper">
                    <span class="status-badge ${badgeColor}"></span>
                    <img>
                </div>
                <div class="card-body d-flex flex-column p-4">
                    <h5 class="card-title fw-bold text-dark mb-3"></h5>

                    <div class="d-flex justify-content-between align-items-end mb-4">
                        <div>
                            <span class="d-block text-muted small mb-1">Oferta Actual</span>
                            <span class="price-tag"></span>
                        </div>
                        <div class="text-end">
                            <span class="d-block text-muted small mb-1">Cierra en</span>
                            <span class="countdown ${isEndingSoon ? "bg-danger text-white" : ""}">
                            </span>
                        </div>
                    </div>

                    <button class="btn btn-puja btn-primary w-100 text-white fw-bold mt-auto" type="button">
                        Ingresar a Pujar
                    </button>
                </div>
            </div>
        `;

        const image = card.querySelector("img");
        image.src = auction.img || "https://placehold.co/600x400?text=SubastaYa";
        image.alt = auction.title;
        image.addEventListener("error", () => {
            image.src = "https://placehold.co/600x400?text=SubastaYa";
        }, { once: true });

        card.querySelector(".status-badge").textContent = badgeIcon;
        card.querySelector(".card-title").textContent = auction.title;
        card.querySelector(".price-tag").textContent = new Intl.NumberFormat("es-AR", {
            style: "currency",
            currency: "ARS",
            maximumFractionDigits: 0
        }).format(auction.currentBid);
        card.querySelector(".countdown").textContent = `⏱ ${formatRemainingTime(remainingMilliseconds)}`;
        card.querySelector("button").addEventListener("click", () => enterLiveRoom(auction.id));

        container.appendChild(card);
    });
}

function getRemainingMilliseconds(endTime) {
    const normalizedEndTime = /(?:Z|[+-]\d{2}:\d{2})$/.test(endTime)
        ? endTime
        : `${endTime}Z`;

    return Math.max(new Date(normalizedEndTime).getTime() - Date.now(), 0);
}

function formatRemainingTime(milliseconds) {
    const totalSeconds = Math.floor(milliseconds / 1000);
    const days = Math.floor(totalSeconds / 86_400);
    const hours = Math.floor((totalSeconds % 86_400) / 3_600);
    const minutes = Math.floor((totalSeconds % 3_600) / 60);
    const seconds = totalSeconds % 60;
    const time = [hours, minutes, seconds]
        .map(value => value.toString().padStart(2, "0"))
        .join(":");

    return days > 0 ? `${days}d ${time}` : time;
}

function enterLiveRoom(auctionId) {
    alert(
        "Navegando a la Sala de Subastas en Vivo para el producto #" +
        auctionId +
        "\n(Acá iría el Módulo 3)"
    );
}
