using System.Data;
using MySqlConnector;

namespace AgroMarket.Api.Data;

public interface IDbConnectionFactory
{
    IDbConnection Create();
}

public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connStr;
    public MySqlConnectionFactory(IConfiguration cfg)
    {
        _connStr = cfg.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少数据库连接串 ConnectionStrings:Default");
    }
    public IDbConnection Create() => new MySqlConnection(_connStr);
}
