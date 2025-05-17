namespace HotChocolate;

internal static class DabPathExtensions
{
    public static int Depth(this Path path)
        => path.Length - 1;

    public static bool IsRootField(this Path path)
        => path.Parent.IsRoot;
}
