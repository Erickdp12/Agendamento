using System.Globalization;
using System.Net.Mail;
using Microsoft.Data.Sqlite;

namespace SistemaAgendamentos;

public static class AgendaEndpoints
{
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz"];

    public static void MapAgenda(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter(HandleStorageErrors);

        api.MapGet("/clientes", (AgendaStore store) => store.ListarClientes());
        api.MapGet("/clientes/{id:long}", (long id, AgendaStore store) =>
            Found(store.BuscarCliente(id), "Cliente não encontrado."));
        api.MapPost("/clientes", CriarCliente);
        api.MapPut("/clientes/{id:long}", AtualizarCliente);

        api.MapGet("/agendamentos", ListarAgendamentos);
        api.MapGet("/agendamentos/{id:long}", (long id, AgendaStore store) =>
            Found(store.BuscarAgendamento(id), "Agendamento não encontrado."));
        api.MapPost("/agendamentos", CriarAgendamento);
        api.MapPut("/agendamentos/{id:long}/horario", Reagendar);
        api.MapPut("/agendamentos/{id:long}/cancelamento", (long id, AgendaStore store, TimeProvider clock) =>
            Found(store.Cancelar(id, clock.GetUtcNow()), "Agendamento não encontrado."));
    }

    private static IResult CriarCliente(ClienteInput input, AgendaStore store)
    {
        var cliente = NormalizarCliente(input);
        if (ValidarCliente(cliente) is { } error)
            return error;

        var created = store.CriarCliente(cliente);
        return Results.Created($"/api/clientes/{created.Id}", created);
    }

    private static IResult AtualizarCliente(long id, ClienteInput input, AgendaStore store)
    {
        var cliente = NormalizarCliente(input);
        if (ValidarCliente(cliente) is { } error)
            return error;

        return Found(store.AtualizarCliente(id, cliente), "Cliente não encontrado.");
    }

    private static ClienteInput NormalizarCliente(ClienteInput input) =>
        new(input.Nome?.Trim(), input.Email?.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(input.Telefone) ? null : input.Telefone.Trim());

    private static IResult? ValidarCliente(ClienteInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Nome is null || input.Nome.Length is 0 or > 120)
            errors["nome"] = ["Informe um nome de 1 a 120 caracteres."];
        if (input.Email is null || input.Email.Length > 254 ||
            !MailAddress.TryCreate(input.Email, out var address) || address.Address != input.Email)
            errors["email"] = ["Informe um e-mail válido de até 254 caracteres."];
        if (input.Telefone is { Length: > 30 })
            errors["telefone"] = ["Informe um telefone de até 30 caracteres."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static IResult ListarAgendamentos(string? de, string? ate, AgendaStore store)
    {
        var errors = new Dictionary<string, string[]>();
        DateTimeOffset? from = de is null ? null : ReadDate(de, "de", errors);
        DateTimeOffset? until = ate is null ? null : ReadDate(ate, "ate", errors);
        if (from.HasValue && until.HasValue && from.Value >= until.Value)
            errors["ate"] = ["O fim do período deve ser posterior ao início."];
        return errors.Count == 0
            ? Results.Ok(store.ListarAgendamentos(from, until))
            : Results.ValidationProblem(errors);
    }

    private static IResult CriarAgendamento(AgendamentoInput input, AgendaStore store, TimeProvider clock)
    {
        var errors = new Dictionary<string, string[]>();
        var (inicio, fim) = ValidarHorario(input.Inicio, input.Fim, clock.GetUtcNow(), errors);
        if (input.ClienteId <= 0)
            errors["clienteId"] = ["Informe o ID de um cliente cadastrado."];
        if (errors.Count != 0)
            return Results.ValidationProblem(errors);

        if (store.BuscarCliente(input.ClienteId) is null)
            return Problem(404, "Cliente não encontrado.");

        var created = store.CriarAgendamento(input.ClienteId, inicio, fim);
        return Results.Created($"/api/agendamentos/{created.Id}", created);
    }

    private static IResult Reagendar(long id, ReagendamentoInput input, AgendaStore store, TimeProvider clock)
    {
        var errors = new Dictionary<string, string[]>();
        var (inicio, fim) = ValidarHorario(input.Inicio, input.Fim, clock.GetUtcNow(), errors);
        if (errors.Count != 0)
            return Results.ValidationProblem(errors);

        var appointment = store.Reagendar(id, inicio, fim);
        if (appointment is not null)
            return Results.Ok(appointment);
        return store.BuscarAgendamento(id) is null
            ? Problem(404, "Agendamento não encontrado.")
            : Problem(409, "Este agendamento já foi cancelado.");
    }

    private static (DateTimeOffset Inicio, DateTimeOffset Fim) ValidarHorario(
        string? inicio, string? fim, DateTimeOffset now, Dictionary<string, string[]> errors)
    {
        var start = ReadDate(inicio, "inicio", errors);
        var end = ReadDate(fim, "fim", errors);
        if (start.HasValue && start.Value <= now)
            errors["inicio"] = ["O início deve estar no futuro."];
        if (start.HasValue && end.HasValue && end.Value <= start.Value)
            errors["fim"] = ["O fim deve ser posterior ao início."];
        return (start.GetValueOrDefault(), end.GetValueOrDefault());
    }

    private static DateTimeOffset? ReadDate(
        string? value, string field, Dictionary<string, string[]> errors)
    {
        if (value?.EndsWith('Z') == true)
            value = value[..^1] + "+00:00";
        if (DateTimeOffset.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return date;

        errors[field] = ["Informe data e hora ISO 8601 com fuso, como 2030-11-20T14:00:00-03:00."];
        return null;
    }

    private static async ValueTask<object?> HandleStorageErrors(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 1811 &&
                                        ex.Message.Contains("agenda_conflito", StringComparison.Ordinal))
        {
            return Problem(409, "Já existe um agendamento ativo nesse horário.");
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            return Problem(409, "Já existe um cliente com esse e-mail.");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            context.HttpContext.Response.Headers["Retry-After"] = "1";
            return Problem(503, "A agenda está ocupada. Tente novamente em instantes.");
        }
    }

    private static IResult Found<T>(T? value, string message) where T : class =>
        value is null ? Problem(404, message) : Results.Ok(value);

    private static IResult Problem(int status, string title) =>
        Results.Problem(statusCode: status, title: title);
}
