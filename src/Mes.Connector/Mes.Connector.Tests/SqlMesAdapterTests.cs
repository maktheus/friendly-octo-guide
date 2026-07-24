using Mes.Connector.Worker;
using Xunit;

namespace Mes.Connector.Tests;

public class SqlMesAdapterTests
{
    [Fact]
    public void SqlMesAdapter_inicializa_com_parametros_padrao()
    {
        var adapter = new SqlMesAdapter("Host=localhost;Database=mes;Username=dev;Password=dev");
        Assert.NotNull(adapter);
    }

    [Fact]
    public void SqlMesAdapter_lança_excecao_quando_connectionString_e_nula()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlMesAdapter(connectionString: null!));
    }
}
