(function (root, factory) {
    const biddingCore = factory();

    if (typeof module === "object" && module.exports) {
        module.exports = biddingCore;
    } else {
        root.SubastaYaBidding = biddingCore;
    }
}(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    const MAX_MONEY_MINOR_UNITS = 999999999999999999n;

    function toMinorUnits(value) {
        const text = String(value ?? "").trim();
        const match = /^(\d+)(?:\.(\d{1,2}))?$/.exec(text);

        if (!match) {
            throw new TypeError("El monto debe ser un número con hasta dos decimales.");
        }

        const wholeUnits = BigInt(match[1]);
        const decimalUnits = BigInt((match[2] ?? "").padEnd(2, "0") || "0");
        const minorUnits = (wholeUnits * 100n) + decimalUnits;

        if (minorUnits > MAX_MONEY_MINOR_UNITS) {
            throw new RangeError("El monto supera el límite decimal(18,2) admitido.");
        }

        return minorUnits;
    }

    function fromMinorUnits(value) {
        if (typeof value !== "bigint" || value < 0n) {
            throw new RangeError("El monto en centavos no es válido.");
        }

        if (value > MAX_MONEY_MINOR_UNITS) {
            throw new RangeError("El monto supera el límite decimal(18,2) admitido.");
        }

        const wholeUnits = value / 100n;
        const decimalUnits = String(value % 100n).padStart(2, "0");

        return `${wholeUnits}.${decimalUnits}`;
    }

    function calculateSuggestedBid(currentAmount, minimumIncrement) {
        const suggestedMinorUnits =
            toMinorUnits(currentAmount) + toMinorUnits(minimumIncrement);

        if (suggestedMinorUnits > MAX_MONEY_MINOR_UNITS) {
            throw new RangeError("La próxima puja supera el límite decimal(18,2) admitido.");
        }

        return fromMinorUnits(suggestedMinorUnits);
    }

    function normalizeBidAmount(value) {
        const minorUnits = toMinorUnits(value);

        if (minorUnits <= 0n) {
            throw new RangeError("La puja debe ser mayor a cero.");
        }

        return fromMinorUnits(minorUnits);
    }

    function formatMoney(value, options = {}) {
        const minorUnits = toMinorUnits(value);
        const normalizedAmount = fromMinorUnits(minorUnits);
        const [wholeUnits, decimalUnits] = normalizedAmount.split(".");
        const groupedWholeUnits = wholeUnits.replace(
            /\B(?=(\d{3})+(?!\d))/g,
            "."
        );
        const currencySymbol =
            String(options.currencySymbol ?? "$").trim() || "$";

        return `${currencySymbol}\u00a0${groupedWholeUnits},${decimalUnits}`;
    }

    function buildBidRequest(baseUrl, auctionId, amount) {
        const normalizedAmount = normalizeBidAmount(amount);
        const normalizedBaseUrl = String(baseUrl).replace(/\/$/, "");
        const encodedAuctionId = encodeURIComponent(String(auctionId));

        return {
            url: `${normalizedBaseUrl}/api/auctions/${encodedAuctionId}/bids`,
            options: {
                method: "POST",
                credentials: "include",
                headers: {
                    "Content-Type": "application/json"
                },
                body: `{"monto":${normalizedAmount}}`
            }
        };
    }

    function buildRoomStateRequest(baseUrl, auctionId) {
        const normalizedBaseUrl = String(baseUrl).replace(/\/$/, "");
        const encodedAuctionId = encodeURIComponent(String(auctionId));

        return {
            url: `${normalizedBaseUrl}/api/auctions/${encodedAuctionId}/bids/state`,
            options: {
                method: "GET",
                cache: "no-store",
                credentials: "include"
            }
        };
    }

    function createBidSubmissionCoordinator(configuration) {
        if (
            typeof configuration?.sendBid !== "function" ||
            typeof configuration?.refreshRoomState !== "function"
        ) {
            throw new TypeError(
                "El coordinador necesita las operaciones sendBid y refreshRoomState."
            );
        }

        let submissionInProgress = false;

        return Object.freeze({
            get isSubmitting() {
                return submissionInProgress;
            },

            async submit(request) {
                if (submissionInProgress) {
                    return {
                        kind: "duplicate"
                    };
                }

                submissionInProgress = true;

                try {
                    const response = await configuration.sendBid(request);

                    if (response.status === 409) {
                        const refreshResult = await configuration.refreshRoomState(
                            response
                        );

                        return {
                            kind: "conflict",
                            response,
                            refreshResult
                        };
                    }

                    return {
                        kind: "response",
                        response
                    };
                } finally {
                    submissionInProgress = false;
                }
            }
        });
    }

    function normalizeBidderStatus(value) {
        const normalized = String(value ?? "")
            .trim()
            .toLocaleLowerCase("es-AR");

        if (normalized === "liderando") {
            return {
                key: "leading",
                label: "Liderando"
            };
        }

        if (normalized === "superado") {
            return {
                key: "outbid",
                label: "Superado"
            };
        }

        return {
            key: "none",
            label: "Sin puja"
        };
    }

    function mergeAuctionWithRoomState(auction, roomState) {
        if (!auction || !roomState) {
            throw new TypeError("La subasta y su estado son obligatorios.");
        }

        return {
            ...auction,
            currentBid: fromMinorUnits(toMinorUnits(roomState.precioActual)),
            minimumIncrement: fromMinorUnits(
                toMinorUnits(roomState.incrementoMinimo)
            ),
            nextMinimum: fromMinorUnits(
                toMinorUnits(roomState.pujaMinimaSiguiente)
            ),
            endDateUtc: roomState.fechaFinUtc,
            status: roomState.estadoSubasta,
            bidderStatus: normalizeBidderStatus(roomState.estadoPostor)
        };
    }

    function createBidUiState() {
        return {
            phase: "idle",
            bidderStatus: normalizeBidderStatus("SinPuja"),
            feedback: "",
            feedbackTone: "neutral",
            blocked: false
        };
    }

    function reduceBidUiState(state, action) {
        const currentState = state ?? createBidUiState();

        switch (action.type) {
            case "ROOM_LOADING":
                return {
                    ...currentState,
                    phase: "loading",
                    feedback: action.message ?? "Actualizando el estado de la subasta…",
                    feedbackTone: action.tone ?? "info",
                    blocked: true
                };

            case "ROOM_LOADED":
                return {
                    ...currentState,
                    phase: "idle",
                    bidderStatus: normalizeBidderStatus(action.bidderStatus),
                    feedback: action.message ?? "",
                    feedbackTone: action.tone ?? "neutral",
                    blocked: false
                };

            case "ROOM_FAILED":
                return {
                    ...currentState,
                    phase: "error",
                    feedback: action.message ?? "No se pudo actualizar la sala.",
                    feedbackTone: "error",
                    blocked: true
                };

            case "BID_SUBMITTING":
                return {
                    ...currentState,
                    phase: "submitting",
                    feedback: action.message ?? "Procesando tu puja…",
                    feedbackTone: "info",
                    blocked: false
                };

            case "BID_SUCCEEDED":
                return {
                    ...currentState,
                    phase: "idle",
                    bidderStatus: normalizeBidderStatus("Liderando"),
                    feedback: action.message ?? "Tu puja fue confirmada.",
                    feedbackTone: "success",
                    blocked: false
                };

            case "BID_CONFLICT":
                return {
                    ...currentState,
                    phase: "refreshing",
                    feedback: action.message ?? "La subasta cambió. Actualizando datos…",
                    feedbackTone: "warning",
                    blocked: true
                };

            case "BID_FAILED":
                return {
                    ...currentState,
                    phase: "idle",
                    feedback: action.message ?? "No se pudo registrar la puja.",
                    feedbackTone: "error",
                    blocked: false
                };

            default:
                return currentState;
        }
    }

    return Object.freeze({
        toMinorUnits,
        fromMinorUnits,
        calculateSuggestedBid,
        normalizeBidAmount,
        formatMoney,
        buildBidRequest,
        buildRoomStateRequest,
        createBidSubmissionCoordinator,
        normalizeBidderStatus,
        mergeAuctionWithRoomState,
        createBidUiState,
        reduceBidUiState
    });
}));
