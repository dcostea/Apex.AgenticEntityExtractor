namespace Apex.AgenticEntityExtractor.Enums;

/// <summary>
/// Represents the possible outcomes of a debate turn between participants.
/// Used to determine whether the debate loop should continue or terminate.
/// </summary>
public enum DebateVerdict
{
  /// <summary>No verdict keyword detected in the response — debate continues.</summary>
  None,

  /// <summary>The participant accepted the current ranking — debate terminates.</summary>
  Approved,

  /// <summary>The participant rejected the current ranking — debate continues with revisions.</summary>
  Rejected,
}
