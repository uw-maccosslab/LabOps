namespace LabOps.Engines;

/// <summary>
/// A problem to tell the user: what they asked for cannot be done, or a record needs fixing first.
/// The message is written for a lab user. The labops tool prints it (or returns it as
/// {"ok": false, "error": ...}) and exits 1; the app shows it.
/// </summary>
public sealed class EngineError(string message) : Exception(message);
