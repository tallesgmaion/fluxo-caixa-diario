import time
from decimal import Decimal
from datetime import date, timedelta
from pathlib import Path
from urllib.parse import urlsplit
from uuid import uuid4

from playwright.sync_api import expect, sync_playwright


def test_fluxo() -> None:
    config = {}
    for line in Path(".env").read_text(encoding="utf-8").splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            config[key] = value
    identificador = uuid4()
    marker = f"Demonstração {identificador.hex[:10]}"
    caixa = (date.today() - timedelta(days=200 + identificador.int % 100_000)).isoformat()
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        sessao = browser.new_context(locale="pt-BR")
        page = sessao.new_page()
        response = page.goto("http://localhost:18083/lancamento")
        assert response.status == 404 and "text/html" in response.headers["content-type"]
        expect(page.get_by_role("heading", name="Página não encontrada")).to_be_visible()
        page.get_by_role("link", name="Voltar ao início").click()
        expect(page).to_have_url("http://localhost:18083/")
        page.goto("http://localhost:18083/lancamentos")
        page.locator("#username").fill("demo")
        page.locator("#password").fill(config["DEMO_PASSWORD"])
        page.locator("#kc-login").click()
        expect(page.get_by_role("heading", name="Novo lançamento")).to_be_visible()
        historico = page.locator("details#historico")
        assert historico.get_attribute("open") is None
        historico.locator("summary").click()
        expect(historico.get_by_role("button", name="Consultar")).to_be_visible()
        consultar_periodo(page, "consolidado", caixa, caixa)
        row = page.locator("tbody tr").last
        saldo_anterior = money(row.get_by_role("cell").nth(3).inner_text())
        page.goto("http://localhost:18083/lancamentos")
        page.get_by_label("Data de caixa").fill(caixa)
        valor = page.get_by_label("Valor (R$)")
        registrar = page.get_by_role("button", name="Registrar")
        expect(registrar).to_be_disabled()
        valor.fill("texto")
        expect(valor).to_have_value("")
        valor.fill("0,00")
        expect(registrar).to_be_disabled()
        valor.fill("12,34")
        expect(registrar).to_be_enabled()
        page.get_by_label("Descrição").fill(marker)
        with page.expect_navigation() as navigation:
            registrar.click()
        assert_redirecionamento(navigation.value)
        expect(page.get_by_role("status")).to_contain_text("Lançamento registrado")
        assert not urlsplit(page.url).query
        consultar_periodo(page, "lancamentos", caixa, caixa)
        expect(page.get_by_text(marker)).to_be_visible()
        assert page.reload().request.method == "GET"
        expect(page.get_by_role("row").filter(has_text=marker)).to_have_count(1)
        credito = page.get_by_role("row").filter(has_text=marker).locator(".credit")
        expect(credito).to_have_css("color", "rgb(23, 97, 67)")
        consultar_periodo(page, "consolidado", caixa, caixa)
        expect(page.get_by_role("heading", name="Consolidado diário")).to_be_visible()
        expect(page.get_by_role("columnheader", name="Saldo do dia (movimento líquido)")).to_be_visible()
        deadline = time.monotonic() + 20
        while money(row.get_by_role("cell").nth(3).inner_text()) != saldo_anterior + Decimal("12.34"):
            if time.monotonic() > deadline:
                raise AssertionError("A projeção do dia não apareceu")
            time.sleep(0.5)
            page.get_by_role("button", name="Consultar").click()
        expect(row.locator(".credit")).to_have_css("color", "rgb(23, 97, 67)")

        inicio = date.fromisoformat(caixa)
        fim = inicio + timedelta(days=24)
        for route in ("lancamentos", "consolidado"):
            consultar_periodo(page, route, str(inicio), str(fim))
            final = page.get_by_label("Fim")
            final.fill(str(inicio - timedelta(days=1)))
            expect(page.get_by_role("button", name="Consultar")).to_be_disabled()
            expect(page.get_by_text("A data final deve ser igual ou posterior à inicial.")).to_be_visible()
            final.fill(str(fim))
            expect(page.get_by_role("button", name="Consultar")).to_be_enabled()

        rows = page.locator("tbody tr")
        expect(rows).to_have_count(10)
        first_page = rows.all_text_contents()
        page.get_by_role("button", name="Próxima").click()
        expect(page.get_by_text("Página 2", exact=True)).to_be_visible()
        expect(rows).to_have_count(10)
        assert rows.all_text_contents() != first_page
        page.get_by_role("button", name="Anterior").click()
        expect(page.get_by_text("Página 1", exact=True)).to_be_visible()
        assert rows.all_text_contents() == first_page
        page.get_by_label("Linhas por página").select_option("20")
        expect(rows).to_have_count(20)
        expect(page.get_by_label("Linhas por página")).to_have_value("20")
        assert not urlsplit(page.url).query
        assert page.request.post("http://localhost:18083/consolidado/consultar", form={
            "Consulta.Inicio": caixa, "Consulta.Fim": caixa,
        }).status == 400
        consultar_periodo(page, "lancamentos", caixa, caixa)
        expect(page.get_by_text(marker)).to_be_visible()
        assert urlsplit(page.url).fragment == "historico"
        assert "no-store" in page.request.get(page.url).headers.get("cache-control", "")
        assert page.request.post("http://localhost:18083/sair").status == 400
        cookie = next(c for c in page.context.cookies() if c["name"] == ".AspNetCore.Cookies")
        expect(page.get_by_role("button", name="Sair")).to_be_visible()
        page.get_by_role("button", name="Sair").click()
        expect(page).to_have_url("http://localhost:18083/")
        expect(page.get_by_role("button", name="Sair")).to_have_count(0)
        page.go_back(wait_until="domcontentloaded")
        expect(page.locator("#kc-login")).to_be_visible()
        expect(page.locator("main table")).to_have_count(0)
        page.close()
        page = sessao.new_page()
        for rota in ("lancamentos", "consolidado"):
            response = page.goto(f"http://localhost:18083/{rota}")
            assert response is not None
            expect(page.locator("#kc-login")).to_be_visible()
            expect(page.locator("main table")).to_have_count(0)
        page.context.add_cookies([cookie])
        response = page.request.get("http://localhost:18083/consolidado", max_redirects=0)
        assert response.status == 302 and "18080" in response.headers["location"]
        browser.close()
    print("Fluxo Blazor, Keycloak, lançamento e consolidação aprovado.")


def money(text: str) -> Decimal:
    return Decimal(text.replace("R$", "").replace(".", "").replace(",", ".").strip())


def consultar_periodo(page, rota: str, inicio: str, fim: str) -> None:
    page.goto(f"http://localhost:18083/{rota}")
    if rota == "lancamentos" and page.locator("#historico").get_attribute("open") is None:
        page.locator("#historico summary").click()
    page.get_by_label("Início").fill(inicio)
    page.get_by_label("Fim").fill(fim)
    with page.expect_navigation() as navigation:
        page.get_by_role("button", name="Consultar").click()
    assert_redirecionamento(navigation.value)
    assert not urlsplit(page.url).query


def assert_redirecionamento(response) -> None:
    assert response.request.method == "GET"
    anterior = response.request.redirected_from
    assert anterior is not None and anterior.method == "POST" and anterior.response().status == 303


if __name__ == "__main__":
    test_fluxo()
