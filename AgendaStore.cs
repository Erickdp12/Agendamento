using Microsoft.Data.Sqlite;

namespace SistemaAgendamentos;

public sealed class AgendaStore(string databasePath)
{
    private const string ClienteColumns = "id, nome, email, telefone";
    private const string AgendaColumns = "id, cliente_id, inicio, fim, cancelado_em";

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        ForeignKeys = true,
        DefaultTimeout = 5
    }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var stream = typeof(AgendaStore).Assembly
            .GetManifestResourceStream("SistemaAgendamentos.Schema.sql")
            ?? throw new InvalidOperationException("Esquema do banco não encontrado.");
        using var schema = new StreamReader(stream);
        using var connection = Open();
        using var command = Command(connection, schema.ReadToEnd());
        command.ExecuteNonQuery();
    }

    public List<Cliente> ListarClientes() =>
        Query($"SELECT {ClienteColumns} FROM clientes ORDER BY nome, id", ReadCliente);

    public Cliente? BuscarCliente(long id) =>
        QueryOne($"SELECT {ClienteColumns} FROM clientes WHERE id = $id",
            ReadCliente, ("$id", id));

    public Cliente CriarCliente(ClienteInput input) =>
        QueryOne($"""
            INSERT INTO clientes (nome, email, telefone) VALUES ($nome, $email, $telefone)
            RETURNING {ClienteColumns}
            """, ReadCliente, ("$nome", input.Nome), ("$email", input.Email),
            ("$telefone", input.Telefone))!;

    public Cliente? AtualizarCliente(long id, ClienteInput input) =>
        QueryOne($"""
            UPDATE clientes SET nome = $nome, email = $email, telefone = $telefone
            WHERE id = $id RETURNING {ClienteColumns}
            """, ReadCliente, ("$id", id), ("$nome", input.Nome), ("$email", input.Email),
            ("$telefone", input.Telefone));

    public List<Agendamento> ListarAgendamentos(DateTimeOffset? de, DateTimeOffset? ate) =>
        Query($"""
            SELECT {AgendaColumns} FROM agendamentos
            WHERE ($de IS NULL OR fim > $de) AND ($ate IS NULL OR inicio < $ate)
            ORDER BY inicio, id
            """, ReadAgendamento, ("$de", de?.UtcTicks), ("$ate", ate?.UtcTicks));

    public Agendamento? BuscarAgendamento(long id) =>
        QueryOne($"SELECT {AgendaColumns} FROM agendamentos WHERE id = $id",
            ReadAgendamento, ("$id", id));

    public Agendamento CriarAgendamento(long clienteId, DateTimeOffset inicio, DateTimeOffset fim) =>
        QueryOne($"""
            INSERT INTO agendamentos (cliente_id, inicio, fim) VALUES ($cliente, $inicio, $fim)
            RETURNING {AgendaColumns}
            """, ReadAgendamento, ("$cliente", clienteId), ("$inicio", inicio.UtcTicks),
            ("$fim", fim.UtcTicks))!;

    public Agendamento? Reagendar(long id, DateTimeOffset inicio, DateTimeOffset fim) =>
        QueryOne($"""
            UPDATE agendamentos SET inicio = $inicio, fim = $fim
            WHERE id = $id AND cancelado_em IS NULL RETURNING {AgendaColumns}
            """, ReadAgendamento, ("$id", id), ("$inicio", inicio.UtcTicks), ("$fim", fim.UtcTicks));

    public Agendamento? Cancelar(long id, DateTimeOffset agora) =>
        QueryOne($"""
            UPDATE agendamentos SET cancelado_em = COALESCE(cancelado_em, $agora)
            WHERE id = $id RETURNING {AgendaColumns}
            """, ReadAgendamento, ("$id", id), ("$agora", agora.UtcTicks));

    private static Cliente ReadCliente(SqliteDataReader row) =>
        new(row.GetInt64(0), row.GetString(1), row.GetString(2),
            row.IsDBNull(3) ? null : row.GetString(3));

    private static Agendamento ReadAgendamento(SqliteDataReader row) =>
        new(row.GetInt64(0), row.GetInt64(1),
            new DateTimeOffset(row.GetInt64(2), TimeSpan.Zero),
            new DateTimeOffset(row.GetInt64(3), TimeSpan.Zero),
            row.IsDBNull(4) ? null : new DateTimeOffset(row.GetInt64(4), TimeSpan.Zero));

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(map(reader));
        return rows;
    }

    private T? QueryOne<T>(string sql, Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters) where T : class =>
        Query(sql, map, parameters).SingleOrDefault();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
