// app.js - Lógica Frontend Vanilla JS

document.addEventListener("DOMContentLoaded", () => {
    // Inicializar la aplicación cargando el catálogo
    fetchAuctions();
    initializeAuctionForm();
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

/**
 * Módulo 2: Crear Subasta.
 * Valida el formulario en el navegador. El envío REST queda pendiente del
 * endpoint POST /api/auctions asignado al módulo de API.
 */
function initializeAuctionForm() {
    const form = document.getElementById("auction-form");

    if (!form) {
        return;
    }

    const startInput = document.getElementById("auction-start");
    const endInput = document.getElementById("auction-end");
    const basePriceInput = document.getElementById("auction-base-price");
    const incrementInput = document.getElementById("auction-minimum-increment");
    const message = document.getElementById("auction-form-message");
    const minimumDate = toDateTimeLocalValue(new Date());

    startInput.min = minimumDate;
    endInput.min = minimumDate;

    const validateDateRange = () => {
        endInput.setCustomValidity("");

        if (!startInput.value || !endInput.value) {
            return;
        }

        const startDate = new Date(startInput.value);
        const endDate = new Date(endInput.value);

        if (endDate <= startDate) {
            endInput.setCustomValidity("La fecha de finalización debe ser posterior a la fecha de inicio.");
        }
    };

    const validatePositiveNumber = input => {
        input.setCustomValidity("");

        if (input.value === "") {
            return;
        }

        const value = Number(input.value);
        if (!Number.isFinite(value) || value <= 0) {
            input.setCustomValidity("El valor debe ser mayor que cero.");
        }
    };

    startInput.addEventListener("change", () => {
        endInput.min = startInput.value || minimumDate;
        validateDateRange();
    });

    endInput.addEventListener("change", validateDateRange);
    basePriceInput.addEventListener("input", () => validatePositiveNumber(basePriceInput));
    incrementInput.addEventListener("input", () => validatePositiveNumber(incrementInput));

    form.addEventListener("submit", event => {
        event.preventDefault();

        validateDateRange();
        validatePositiveNumber(basePriceInput);
        validatePositiveNumber(incrementInput);
        form.classList.add("was-validated");

        if (!form.checkValidity()) {
            showAuctionFormMessage(
                message,
                "Revisá los campos marcados antes de publicar la subasta.",
                "danger"
            );
            form.querySelector(":invalid")?.focus();
            return;
        }

        showAuctionFormMessage(
            message,
            "La subasta pasó todas las validaciones. Quedó lista para enviarse cuando el endpoint de publicación esté disponible.",
            "success"
        );
    });

    form.addEventListener("reset", () => {
        window.setTimeout(() => {
            form.classList.remove("was-validated");
            startInput.setCustomValidity("");
            endInput.setCustomValidity("");
            basePriceInput.setCustomValidity("");
            incrementInput.setCustomValidity("");
            endInput.min = minimumDate;
            message.className = "alert d-none";
            message.textContent = "";
        }, 0);
    });
}

function showAuctionFormMessage(element, text, type) {
    element.textContent = text;
    element.className = `alert alert-${type}`;
}

function toDateTimeLocalValue(date) {
    const timezoneOffset = date.getTimezoneOffset() * 60_000;
    return new Date(date.getTime() - timezoneOffset).toISOString().slice(0, 16);
}
