using StrandsAgents.Core;

namespace StrandsAgents.Tools;

/// <summary>
/// Built-in calculator tool. Performs basic arithmetic on two numbers.
/// The source generator emits <c>CalculatorTool_Calculate_Tool</c> — an
/// <see cref="ITool"/> wrapper with a compile-time JSON schema.
/// </summary>
public sealed partial class CalculatorTool
{
    /// <summary>
    /// Performs basic arithmetic on two numbers.
    /// </summary>
    /// <param name="a">The left-hand operand.</param>
    /// <param name="operation">
    /// The operation to perform. Accepted values: add, subtract, multiply, divide
    /// (and common symbols +, -, *, /).
    /// </param>
    /// <param name="b">The right-hand operand.</param>
    /// <returns>The numeric result as a string.</returns>
    [Tool("Performs basic arithmetic (add, subtract, multiply, divide) on two numbers.")]
    public double Calculate(double a, string operation, double b)
    {
        return operation.Trim().ToLowerInvariant() switch
        {
            "add" or "addition" or "+" => a + b,
            "subtract" or "subtraction" or "-" => a - b,
            "multiply" or "multiplication" or "times" or "*" or "x" => a * b,
            "divide" or "division" or "/" => b == 0
                ? throw new DivideByZeroException("Cannot divide by zero.")
                : a / b,
            _ => throw new ArgumentException(
                $"Unknown operation '{operation}'. Supported: add, subtract, multiply, divide.")
        };
    }
}
