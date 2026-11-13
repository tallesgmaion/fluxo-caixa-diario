function formatarValor(valor) {
    const [inteiro, centavos = ""] = valor.split(",");
    return (inteiro || "0") + "," + centavos.padEnd(2, "0");
}

const processamento = document.querySelector("#processamento");
const mensagemProcessamento = processamento.querySelector("#mensagem-processamento");

function iniciarProcessamento(event, mensagem) {
    if (event.defaultPrevented) return;
    if (processamento.open) {
        event.preventDefault();
        return;
    }
    mensagemProcessamento.textContent = mensagem;
    processamento.showModal();
}

processamento.addEventListener("cancel", (event) => event.preventDefault());
if (document.querySelector('form[action="/sair"]')) {
    // Keep a protected document hidden if the browser restores it from history.
    window.addEventListener("pagehide", () => { document.body.hidden = true; });
}
window.addEventListener("pageshow", (event) => {
    if (event.persisted && document.body.hidden) {
        location.reload();
        return;
    }
    if (processamento.open) processamento.close();
});

for (const form of document.querySelectorAll('[data-formulario="lancamento"]')) {
    const valor = form.querySelector("[data-moeda]");
    const registrar = form.querySelector('button[type="submit"]');
    let ultimoValor = valor.value;

    function validar() {
        const positivo = /[1-9]/.test(valor.value);
        valor.setCustomValidity(positivo || !valor.value ? "" : "Informe um valor maior que zero.");
        registrar.disabled = !form.checkValidity();
    }

    valor.addEventListener("input", () => {
        const texto = valor.value.replace(".", ",");
        const [inteiro, centavos] = texto.split(",");
        const normalizado = inteiro.replace(/^0+(?=\d)/, "");
        const aceito = /^\d{0,17}(,\d{0,2})?$/.test(normalizado + (centavos === undefined ? "" : "," + centavos))
            && texto.split(",").length <= 2;
        const corrigido = aceito ? normalizado + (centavos === undefined ? "" : "," + centavos) : ultimoValor;
        if (valor.value !== corrigido) {
            const posicao = Math.max(0, valor.selectionStart - (valor.value.length - corrigido.length));
            valor.value = corrigido;
            valor.setSelectionRange(posicao, posicao);
        }
        ultimoValor = valor.value;
    });
    valor.addEventListener("blur", () => {
        if (valor.value) valor.value = formatarValor(valor.value);
        ultimoValor = valor.value;
        validar();
    });
    form.addEventListener("input", validar);
    form.addEventListener("change", validar);
    form.addEventListener("submit", (event) => {
        validar();
        if (!form.reportValidity()) {
            event.preventDefault();
            return;
        }
        valor.value = formatarValor(valor.value);
        iniciarProcessamento(event, "Registrando lançamento…");
    });
    validar();
}

for (const form of document.querySelectorAll('[data-formulario="periodo"]')) {
    const inicio = form.querySelector("[data-inicio]");
    const fim = form.querySelector("[data-fim]");
    const botao = form.querySelector('button[type="submit"]');
    const aviso = form.querySelector("[data-erro-periodo]");
    const maxDias = Number(form.dataset.maxDias);

    function validar() {
        if (fim.value) inicio.max = fim.value;
        else inicio.removeAttribute("max");
        if (inicio.value) fim.min = inicio.value;
        else fim.removeAttribute("min");

        let mensagem = "";
        if (inicio.value && fim.value) {
            if (fim.value < inicio.value) mensagem = "A data final deve ser igual ou posterior à inicial.";
            else if (maxDias && (fim.valueAsNumber - inicio.valueAsNumber) / 86400000 + 1 > maxDias)
                mensagem = "Selecione um período de até " + maxDias + " dias.";
        }
        fim.setCustomValidity(mensagem);
        aviso.textContent = mensagem;
        aviso.hidden = !mensagem;
        botao.disabled = !form.checkValidity();
    }

    form.addEventListener("input", validar);
    form.addEventListener("change", validar);
    form.addEventListener("submit", (event) => {
        validar();
        if (!form.reportValidity()) {
            event.preventDefault();
            return;
        }
        iniciarProcessamento(event, "Consultando…");
    });
    validar();
}

for (const form of document.querySelectorAll('[data-formulario="paginacao"]')) {
    form.querySelector("[data-limite]").addEventListener("change", () => form.requestSubmit());
    form.addEventListener("submit", (event) => iniciarProcessamento(event, "Consultando…"));
}
