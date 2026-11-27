namespace Chitragupta.Application;

/// <summary>Something asked for does not exist. The API answers 404.</summary>
public sealed class NotFoundException(string what) : Exception($"{what} was not found.");

/// <summary>A request that cannot be carried out as given. The API answers 400 with the message.</summary>
public sealed class RequestRejectedException(string message) : Exception(message);
