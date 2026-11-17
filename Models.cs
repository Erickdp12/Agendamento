namespace SistemaAgendamentos;

public sealed record LoginRequest(string? Usuario, string? Senha);
public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);
public sealed record ClienteInput(string? Nome, string? Email, string? Telefone);
public sealed record Cliente(long Id, string Nome, string Email, string? Telefone);
public sealed record AgendamentoInput(long ClienteId, string? Inicio, string? Fim);
public sealed record ReagendamentoInput(string? Inicio, string? Fim);

public sealed record Agendamento(
    long Id, long ClienteId, DateTimeOffset Inicio, DateTimeOffset Fim,
    DateTimeOffset? CanceladoEm)
{
    public string Status => CanceladoEm.HasValue ? "cancelado" : "ativo";
}
