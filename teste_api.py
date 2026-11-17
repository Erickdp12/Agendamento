"""Teste de integração para uma instância local com banco de teste."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from getpass import getpass
import json
from threading import Barrier
from urllib import error, parse, request
from uuid import uuid4

BASE = "http://localhost:5080/api"


def chamada(metodo, rota, esperado, corpo=None, token=None):
    headers = {}
    dados = None
    if corpo is not None:
        headers["Content-Type"] = "application/json"
        dados = json.dumps(corpo).encode()
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = request.Request(BASE + rota, data=dados, headers=headers, method=metodo)
    try:
        resposta = request.urlopen(req, timeout=15)
    except error.HTTPError as exc:
        resposta = exc
    with resposta:
        codigo = resposta.status
        bruto = resposta.read()
        validos = esperado if isinstance(esperado, tuple) else (esperado,)
        if codigo not in validos:
            raise AssertionError(f"{metodo} {rota} retornou {codigo}; esperado {validos}: {bruto!r}")
        return codigo, json.loads(bruto) if bruto else None


def main():
    chamada("GET", "/agendamentos", 401)
    chamada("GET", "/agendamentos", 401, token="token-invalido")
    login = {"usuario": input("Usuário [admin]: ").strip() or "admin", "senha": getpass("Senha: ")}
    _, acesso = chamada("POST", "/login", 200, login)
    token = acesso["accessToken"]

    def api(metodo, rota, esperado, corpo=None):
        return chamada(metodo, rota, esperado, corpo, token)

    cliente = {"nome": "Cliente de teste", "email": f"teste-{uuid4().hex}@example.com"}
    _, registro = api("POST", "/clientes", 201, cliente)
    api("POST", "/clientes", 409, cliente)
    rota_cliente = f"/clientes/{registro['id']}"
    api("PUT", rota_cliente, 200, {**cliente, "telefone": "11999990000"})
    _, atualizado = api("GET", rota_cliente, 200)
    assert atualizado["telefone"] == "11999990000"

    agora = datetime.now(timezone.utc).replace(microsecond=0)
    inicio = agora + timedelta(days=3650 + uuid4().int % 5000)
    fim = inicio + timedelta(hours=1)

    def payload(de, ate):
        return {"clienteId": registro["id"], "inicio": de.isoformat(), "fim": ate.isoformat()}

    reserva = payload(inicio, fim)
    api("POST", "/agendamentos", 400, {**reserva, "fim": inicio.isoformat()})
    api("POST", "/agendamentos", 400, {**reserva, "inicio": inicio.replace(tzinfo=None).isoformat()})

    barreira = Barrier(2)

    def reservar(_):
        barreira.wait(timeout=5)
        return api("POST", "/agendamentos", (201, 409), reserva)

    with ThreadPoolExecutor(max_workers=2) as executor:
        resultados = sorted(executor.map(reservar, range(2)), key=lambda item: item[0])

    assert [codigo for codigo, _ in resultados] == [201, 409], resultados
    original = resultados[0][1]
    rota_original = f"/agendamentos/{original['id']}"
    api("POST", "/agendamentos", 409, payload(inicio - timedelta(minutes=15), fim + timedelta(minutes=15)))

    _, seguinte = api("POST", "/agendamentos", 201, payload(fim, fim + timedelta(hours=1)))
    rota_seguinte = f"/agendamentos/{seguinte['id']}"
    horario = {"inicio": inicio.isoformat(), "fim": fim.isoformat()}
    api("PUT", rota_seguinte + "/horario", 409, horario)

    _, cancelada = api("PUT", rota_original + "/cancelamento", 200)
    _, repetida = api("PUT", rota_original + "/cancelamento", 200)
    assert cancelada["status"] == "cancelado" and cancelada["canceladoEm"]
    assert cancelada == repetida
    api("PUT", rota_original + "/horario", 409, horario)

    _, alterada = api("PUT", rota_seguinte + "/horario", 200, horario)
    assert datetime.fromisoformat(alterada["inicio"]) == inicio
    _, liberada = api("POST", "/agendamentos", 201, payload(fim, fim + timedelta(hours=1)))
    filtro = parse.urlencode({"de": inicio.isoformat(), "ate": fim.isoformat()})
    _, periodo = api("GET", f"/agendamentos?{filtro}", 200)
    assert {reserva["id"] for reserva in periodo} == {original["id"], seguinte["id"]}
    api("PUT", rota_seguinte + "/cancelamento", 200)
    api("PUT", f"/agendamentos/{liberada['id']}/cancelamento", 200)
    print("Verificações da API concluídas.")


if __name__ == "__main__":
    main()
