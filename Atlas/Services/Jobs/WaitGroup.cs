namespace Atlas.Services.Jobs;

/// <summary>
/// A named scope that jobs register with. Callers can await WaitForAsync on any WaitGroup
/// to block until all jobs registered under that group have completed.
/// A single job can participate in multiple groups — e.g. a SaleRecalcJob registers with
/// both ("SaleRecalc", saleId) for fine-grained awaiting and ("Sale", saleId) for coarse.
/// </summary>
public record WaitGroup(string Type, string Key);
