namespace Dirvexa.Core;

public static class SaturatingMath
{
  public static long AddNonNegative(long current, long value, out bool saturated)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(current);
    ArgumentOutOfRangeException.ThrowIfNegative(value);
    saturated = value > long.MaxValue - current;
    return saturated ? long.MaxValue : current + value;
  }
}
