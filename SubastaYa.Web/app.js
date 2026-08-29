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
        // DESCOMENTAR PARA USAR LA API REAL CUANDO ESTÉ LISTA:
        /*
        const response = await fetch(API_URL);
        if (!response.ok) {
            throw new Error(`Error HTTP: ${response.status}`);
        }
        const auctions = await response.json();
        */

        // --- DATOS MOCK PARA MAQUETACIÓN (Eliminar al conectar backend) ---
        const mockAuctions = [
            {
                id: 1,
                title: "MacBook Pro M2 16GB",
                currentBid: 850000,
                endTime: "00:45:10",
                status: "Activa",
                img: "https://images.unsplash.com/photo-1517336714731-489689fd1ca8?auto=format&fit=crop&w=600"
            },
            {
                id: 2,
                title: "Silla Gamer Noblechairs",
                currentBid: 120000,
                endTime: "02:15:00",
                status: "Activa",
                img: "https://images.unsplash.com/photo-1598550476439-6847785fcea6?auto=format&fit=crop&w=600"
            },
            {
                id: 3,
                title: "Monitor UltraWide LG 34'",
                currentBid: 340000,
                endTime: "00:00:30",
                status: "Fuego",
                img: "https://images.unsplash.com/photo-1527443224154-c4a3942d3acf?auto=format&fit=crop&w=600"
            }
        ];

        // Simulamos tiempo de carga de red (1 segundo) para ver el spinner
        await new Promise(resolve => setTimeout(resolve, 1000));

        // Ocultar el spinner
        spinner.style.display = "none";

        // Renderizar las tarjetas en el DOM
        renderAuctions(mockAuctions, container);
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
    auctions.forEach(auction => {
        // Lógica visual para subastas por terminar
        const isEndingSoon = auction.endTime.startsWith("00:00");
        const badgeColor = isEndingSoon ? "text-danger" : "text-primary";
        const badgeIcon = isEndingSoon ? "🔥 Por terminar" : "✅ Activa";

        const card = document.createElement("div");
        card.className = "col-12 col-md-6 col-lg-4";
        card.innerHTML = `
            <div class="card shadow-sm auction-card h-100">
                <div class="auction-img-wrapper">
                    <span class="status-badge ${badgeColor}">${badgeIcon}</span>
                    <img src="${auction.img}" alt="${auction.title}">
                </div>
                <div class="card-body d-flex flex-column p-4">
                    <h5 class="card-title fw-bold text-dark mb-3">${auction.title}</h5>

                    <div class="d-flex justify-content-between align-items-end mb-4">
                        <div>
                            <span class="d-block text-muted small mb-1">Oferta Actual</span>
                            <span class="price-tag">$${auction.currentBid.toLocaleString("es-AR")}</span>
                        </div>
                        <div class="text-end">
                            <span class="d-block text-muted small mb-1">Cierra en</span>
                            <span class="countdown ${isEndingSoon ? "bg-danger text-white" : ""}">
                                ⏱ ${auction.endTime}
                            </span>
                        </div>
                    </div>

                    <button class="btn btn-puja btn-primary w-100 text-white fw-bold mt-auto" onclick="enterLiveRoom(${auction.id})">
                        Ingresar a Pujar
                    </button>
                </div>
            </div>
        `;
        container.appendChild(card);
    });
}

function enterLiveRoom(auctionId) {
    alert(
        "Navegando a la Sala de Subastas en Vivo para el producto #" +
        auctionId +
        "\n(Acá iría el Módulo 3)"
    );
}
