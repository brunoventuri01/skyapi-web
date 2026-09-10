// Funções de navegador usadas pelo SkyAPI Web.
// Nada aqui grava credenciais: só preferências de aparência, o cadeado entre abas,
// o aviso de saída durante operações e o download dos relatórios gerados em memória.
(function () {
    "use strict";

    var PREFS = "skyapi.preferencias";   // { tema: "escuro"|"claro", escala: 1.0 }
    var LOCK = "skyapi.conexao";         // marca de aba conectada, para o modo de reserva
    var LOCK_TTL = 15000;                // uma marca sem renovação por 15 s é considerada abandonada
    var LOCK_BEAT = 5000;

    var lockRelease = null;   // resolve() da promessa que mantém o Web Lock preso
    var lockDone = null;      // promessa de navigator.locks.request: só cumpre quando o cadeado sai
    var lockId = null;        // identificador desta aba no modo de reserva
    var lockTimer = null;
    var busy = false;

    // ---------------------------------------------------------------- saída
    function onBeforeUnload(e) {
        // O navegador mostra o texto padrão dele; a string só liga o aviso.
        e.preventDefault();
        e.returnValue = "Há operações em andamento. Sair agora encerra a sessão.";
        return e.returnValue;
    }

    // -------------------------------------------------- cadeado entre abas
    // Preferência: Web Locks, que o próprio navegador libera se a aba morrer.
    // Reserva: uma marca em localStorage renovada periodicamente.
    function takeWebLock() {
        return new Promise(function (resolve) {
            var settled = false;
            // request() devolve a promessa antes de chamar o callback, então lockDone já fica guardada.
            lockDone = navigator.locks.request("skyapi-conexao", { ifAvailable: true }, function (lock) {
                if (!lock) { settled = true; resolve(false); return Promise.resolve(); }
                return new Promise(function (release) {
                    lockRelease = release;
                    settled = true;
                    resolve(true);
                });
            }).catch(function () { if (!settled) { settled = true; resolve(false); } });
        });
    }

    function readLock() {
        try {
            var raw = window.localStorage.getItem(LOCK);
            if (!raw) return null;
            var value = JSON.parse(raw);
            if (!value || typeof value.id !== "string" || typeof value.ts !== "number") return null;
            return value;
        } catch (e) { return null; }
    }

    function writeLock() {
        try { window.localStorage.setItem(LOCK, JSON.stringify({ id: lockId, ts: Date.now() })); }
        catch (e) { /* aba anônima ou armazenamento bloqueado: segue sem o cadeado de reserva */ }
    }

    function takeFallbackLock() {
        var current = readLock();
        if (current && current.id !== lockId && Date.now() - current.ts < LOCK_TTL) return false;
        lockId = (Math.random().toString(36) + Date.now().toString(36)).slice(2);
        writeLock();
        if (lockTimer) window.clearInterval(lockTimer);
        lockTimer = window.setInterval(writeLock, LOCK_BEAT);
        return true;
    }

    function dropFallbackLock() {
        if (lockTimer) { window.clearInterval(lockTimer); lockTimer = null; }
        var current = readLock();
        if (current && current.id === lockId) { try { window.localStorage.removeItem(LOCK); } catch (e) { } }
        lockId = null;
    }

    window.sky = {
        // Reserva a conexão para esta aba. Devolve false se outra aba já estiver conectada.
        lock: function () {
            if (lockRelease || lockId) return Promise.resolve(true);   // esta aba já tem o cadeado
            if (window.navigator && window.navigator.locks && window.isSecureContext) return takeWebLock();
            return Promise.resolve(takeFallbackLock());
        },

        // Devolve uma promessa que só cumpre depois de o cadeado sair de fato: sem isso,
        // reconectar na mesma aba encontraria o próprio cadeado ainda preso.
        unlock: function () {
            dropFallbackLock();
            if (!lockRelease) return Promise.resolve();
            var release = lockRelease, done = lockDone;
            lockRelease = null;
            lockDone = null;
            release();
            return done || Promise.resolve();
        },

        // Liga ou desliga o aviso de saída enquanto há operações em andamento.
        busy: function (running) {
            running = !!running;
            if (running === busy) return;
            busy = running;
            if (running) window.addEventListener("beforeunload", onBeforeUnload);
            else window.removeEventListener("beforeunload", onBeforeUnload);
        },

        // Aparência gravada no navegador. Sem nada gravado, segue o tema do sistema.
        preferences: function () {
            var dark = !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
            var scale = 1;
            try {
                var raw = window.localStorage.getItem(PREFS);
                if (raw) {
                    var saved = JSON.parse(raw);
                    if (saved && typeof saved.tema === "string") dark = saved.tema === "escuro";
                    if (saved && typeof saved.escala === "number" && isFinite(saved.escala)) scale = saved.escala;
                }
            } catch (e) { /* preferência é conveniência: sem ela vale o padrão */ }
            return { dark: dark, scale: Math.min(1.4, Math.max(0.9, scale)) };
        },

        savePreferences: function (dark, scale) {
            try {
                window.localStorage.setItem(PREFS, JSON.stringify({
                    tema: dark ? "escuro" : "claro",
                    escala: Math.min(1.4, Math.max(0.9, Number(scale) || 1))
                }));
            } catch (e) { /* idem: falha ao gravar não interrompe o uso */ }
        },

        // Entrega um arquivo gerado em memória. Nada é enviado para fora do navegador.
        download: function (name, content) {
            var blob = new Blob(["\uFEFF" + content], { type: "text/csv;charset=utf-8" });
            var url = window.URL.createObjectURL(blob);
            var link = document.createElement("a");
            link.href = url;
            link.download = name;
            link.rel = "noopener";
            link.style.display = "none";
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            window.setTimeout(function () { window.URL.revokeObjectURL(url); }, 20000);
        }
    };

    // Fechar a aba libera o cadeado de reserva para a próxima.
    window.addEventListener("pagehide", function () { dropFallbackLock(); });
})();
