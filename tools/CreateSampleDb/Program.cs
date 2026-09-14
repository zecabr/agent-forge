using Microsoft.Data.Sqlite;

// Utilitário pra criar um banco SQLite de exemplo pro sql-reader capability,
// pra quem não tem sqlite3.exe no PATH.
//
// Uso: dotnet run --project tools/CreateSampleDb -- --db data/app.db
//
// Idempotente: apaga o arquivo antes se já existir.

var dbPath = ParseDbPath(args) ?? Path.Combine("data", "app.db");
var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

if (File.Exists(dbPath))
{
    Console.WriteLine($"[create-sample-db] removing existing {dbPath}");
    File.Delete(dbPath);
}

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWriteCreate,
}.ToString();

await using var conn = new SqliteConnection(connectionString);
await conn.OpenAsync();

const string schema = """
    CREATE TABLE customer(
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        city TEXT DEFAULT 'unknown',
        created_at TEXT NOT NULL
    );

    CREATE TABLE orders(
        id INTEGER PRIMARY KEY,
        customer_id INTEGER NOT NULL REFERENCES customer(id),
        amount REAL NOT NULL,
        status TEXT NOT NULL,
        placed_at TEXT NOT NULL
    );

    CREATE INDEX idx_orders_customer ON orders(customer_id);

    INSERT INTO customer(id, name, city, created_at) VALUES
        (1, 'Zeca',    'Blumenau', '2026-01-15'),
        (2, 'Alice',   'Curitiba', '2026-02-20'),
        (3, 'Bob',     'Recife',   '2026-03-05'),
        (4, 'Camila',  'Blumenau', '2026-04-10'),
        (5, 'Diego',   'Curitiba', '2026-05-22');

    INSERT INTO orders(id, customer_id, amount, status, placed_at) VALUES
        (1, 1, 100.50, 'paid',     '2026-06-01'),
        (2, 1, 200.00, 'paid',     '2026-06-15'),
        (3, 2,  50.25, 'pending',  '2026-07-03'),
        (4, 4, 350.75, 'paid',     '2026-07-18'),
        (5, 4,  25.00, 'refunded', '2026-08-02'),
        (6, 5, 999.99, 'paid',     '2026-09-10');
    """;

await using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = schema;
    await cmd.ExecuteNonQueryAsync();
}

Console.WriteLine($"[create-sample-db] created {dbPath}");
Console.WriteLine("  · tables: customer (5 rows), orders (6 rows)");
Console.WriteLine("  · try: SELECT c.name, SUM(o.amount) FROM customer c JOIN orders o ON o.customer_id=c.id GROUP BY c.name");
return 0;

static string? ParseDbPath(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--db")
        {
            return args[i + 1];
        }
    }

    return null;
}
