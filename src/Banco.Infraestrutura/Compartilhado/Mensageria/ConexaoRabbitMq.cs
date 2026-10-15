using RabbitMQ.Client;

namespace Banco.Infraestrutura.Compartilhado.Mensageria;

/// <summary>
/// Conexão única (singleton) com o RabbitMQ, aberta sob demanda. Depois de aberta, quedas são tratadas pela
/// <i>automatic recovery</i> do cliente (conexão, canais e topologia — design D8). A primeira abertura não tem
/// recovery: se o broker estiver fora, <see cref="ObterAsync"/> falha e quem chama tenta de novo depois — é o que
/// deixa o serviço subir e atender sem o broker.
/// </summary>
public sealed class ConexaoRabbitMq(OpcoesRabbitMq opcoes) : IAsyncDisposable
{
    private readonly SemaphoreSlim _trava = new(1, 1);
    private IConnection? _conexao;

    public async Task<IConnection> ObterAsync(CancellationToken cancelamento)
    {
        if (_conexao is { IsOpen: true } aberta)
        {
            return aberta;
        }

        await _trava.WaitAsync(cancelamento);
        try
        {
            if (_conexao is { IsOpen: true } jaAberta)
            {
                return jaAberta;
            }

            // Fechada de vez (ex.: recovery desistiu): descarta e abre outra.
            if (_conexao is not null)
            {
                await _conexao.DisposeAsync();
                _conexao = null;
            }

            var fabrica = new ConnectionFactory
            {
                HostName = opcoes.Host,
                Port = opcoes.Porta,
                UserName = opcoes.Usuario,
                Password = opcoes.Senha,
                ClientProvidedName = opcoes.NomeCliente,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            };
            _conexao = await fabrica.CreateConnectionAsync(cancelamento);
            return _conexao;
        }
        finally
        {
            _trava.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
        }

        _trava.Dispose();
    }
}
