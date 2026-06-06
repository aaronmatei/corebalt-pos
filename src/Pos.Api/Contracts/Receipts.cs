using Pos.Application.Receipts;

namespace Pos.Api.Contracts;

/// <summary>
/// The receipt for a completed sale: the structured <see cref="ReceiptModel"/>, the rendered
/// fixed-width thermal text, and a simple HTML preview for the till — all projected from the
/// persisted sale (never recomputed).
/// </summary>
public sealed record ReceiptResponse(ReceiptModel Model, string Text, string Html, int Columns);
