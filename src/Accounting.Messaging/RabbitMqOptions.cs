using System.ComponentModel.DataAnnotations;
using RabbitMQ.Client;

namespace Accounting.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    [Required]
    public string VirtualHost { get; init; } = string.Empty;

    [Required]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    public Task<IConnection> ConnectAsync(string clientName, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            VirtualHost = VirtualHost,
            UserName = UserName,
            Password = Password,
            ClientProvidedName = clientName,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }
}
