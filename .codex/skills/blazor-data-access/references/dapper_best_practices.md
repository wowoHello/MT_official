# Dapper ORM 最佳實踐

## 1. 非同步操作 (Async/Await)

始終使用 Dapper 的非同步方法（例如 `QueryAsync`, `ExecuteAsync`）來執行資料庫操作。這可以避免在 I/O 等待期間阻塞應用程式的執行緒，特別是在 Blazor Server 應用程式中，這對於保持 UI 響應性和伺服器效能至關重要。

```csharp
public async Task<IEnumerable<User>> GetAllUsersAsync()
{
    using (var connection = _connectionFactory.CreateConnection())
    {
        return await connection.QueryAsync<User>("SELECT * FROM Users");
    }
}

public async Task AddUserAsync(User user)
{
    using (var connection = _connectionFactory.CreateConnection())
    {
        var sql = "INSERT INTO Users (Username, Email, PasswordHash) VALUES (@Username, @Email, @PasswordHash)";
        await connection.ExecuteAsync(sql, user);
    }
}
```

## 2. 參數化查詢

使用 Dapper 的參數化查詢功能來防止 SQL 注入攻擊，並提高查詢效能。Dapper 會自動將模型物件的屬性映射到 SQL 查詢中的參數。

```csharp
public async Task<User> GetUserByIdAsync(int id)
{
    using (var connection = _connectionFactory.CreateConnection())
    {
        return await connection.QuerySingleOrDefaultAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = id });
    }
}
```

## 3. 連線管理

確保資料庫連線在使用完畢後被正確關閉和釋放。使用 `using` 語句可以確保 `IDbConnection` 物件在離開作用域時被自動處置。

```csharp
// 建議使用連線工廠模式來管理連線字串和連線建立
public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}
```

## 4. 多重映射 (Multi-Mapping)

Dapper 支援將單一查詢結果映射到多個物件。這對於處理具有一對一或一對多關係的複雜物件非常有用。

```csharp
public class Order
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public Customer Customer { get; set; }
}

public class Customer
{
    public int CustomerId { get; set; }
    public string Name { get; set; }
}

public async Task<IEnumerable<Order>> GetOrdersWithCustomersAsync()
{
    var sql = "SELECT o.*, c.* FROM Orders o INNER JOIN Customers c ON o.CustomerId = c.CustomerId";
    using (var connection = _connectionFactory.CreateConnection())
    {
        return await connection.QueryAsync<Order, Customer, Order>(sql, (order, customer) =>
        {
            order.Customer = customer;
            return order;
        }, splitOn: "CustomerId");
    }
}
```

## 5. 緩存 (Caching)

對於不經常變動的資料，可以考慮在服務層實施緩存機制（例如使用 `IMemoryCache` 或 Redis），以減少資料庫負載並提高應用程式響應速度。

## 6. 錯誤處理與日誌記錄

在資料存取層中實施健壯的錯誤處理和日誌記錄。捕獲資料庫操作可能拋出的異常，並記錄詳細的錯誤資訊，以便於問題診斷和解決。

## 7. 事務處理 (Transactions)

對於需要多個資料庫操作原子性執行的場景，使用事務來確保資料的一致性。

```csharp
public async Task TransferFundsAsync(int fromAccountId, int toAccountId, decimal amount)
{
    using (var connection = _connectionFactory.CreateConnection())
    {
        connection.Open();
        using (var transaction = connection.BeginTransaction())
        {
            try
            {
                await connection.ExecuteAsync("UPDATE Accounts SET Balance = Balance - @Amount WHERE Id = @FromAccountId", new { Amount = amount, FromAccountId = fromAccountId }, transaction: transaction);
                await connection.ExecuteAsync("UPDATE Accounts SET Balance = Balance + @Amount WHERE Id = @ToAccountId", new { Amount = amount, ToAccountId = toAccountId }, transaction: transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
```
