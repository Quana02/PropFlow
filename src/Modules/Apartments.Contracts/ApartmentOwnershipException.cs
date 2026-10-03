namespace PropFlow.Modules.Apartments.Contracts;

public sealed class ApartmentOwnershipException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
