using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>One kind of card. Implementations are registered in DI and discovered by <see cref="ICardRegistry"/>.</summary>
public interface ICardType
{
    CardTypeInfo Info { get; }

    /// <summary>Runs the card and returns its output (passed to downstream cards as their input) and, for branching cards, the fired output handle.</summary>
    /// <exception cref="ArgumentException">Bad configuration (shown on the card as its error).</exception>
    /// <exception cref="InvalidOperationException">The work itself failed (shown on the card as its error).</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct);
}
