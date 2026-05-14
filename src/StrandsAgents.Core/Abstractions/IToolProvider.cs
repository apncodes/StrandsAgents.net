namespace StrandsAgents.Core;

/// <summary>
/// Implemented by classes that host one or more <see cref="ITool"/> methods decorated
/// with <see cref="ToolAttribute"/>. The source generator emits a <c>partial class</c>
/// implementation automatically when the class is declared <c>partial</c>;
/// users never write this method by hand.
/// </summary>
public interface IToolProvider
{
    /// <summary>Returns all <see cref="ITool"/> instances hosted by this provider.</summary>
    IEnumerable<ITool> GetTools();
}
