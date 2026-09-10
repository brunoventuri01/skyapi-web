(function () {
    "use strict";

    var SPEC_URL = "./docs/openapi.json";
    var CUSTOM_URL = "./v1/doc/customization";
    var OPS = {};

    var DEFAULTS = {
        appName: "Skynova",
        apiUrl: "https://api.skymail.net.br/v1",
        apiHost: "api.skymail.net.br",
        panelUrl: "https://painel.skymail.com.br",
        panelHost: "painel.skymail.com.br"
    };

    var CUSTOM = DEFAULTS;

    var METHOD_COLOR = {
        GET: "green", POST: "blue", PUT: "yellow",
        PATCH: "purple", DELETE: "red", HEAD: "gray", OPTIONS: "gray"
    };

    function esc(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    }

    function copyText(text) {
        try { if (navigator.clipboard) { navigator.clipboard.writeText(text); return; } } catch (e) { /* fallback */ }
        var ta = document.createElement("textarea");
        ta.value = text; ta.style.position = "fixed"; ta.style.opacity = "0";
        document.body.appendChild(ta); ta.select();
        try { document.execCommand("copy"); } catch (e) { /* ignore */ }
        document.body.removeChild(ta);
    }

    // Insere um botão "Copiar" no canto superior direito de cada bloco <pre>.
    function addCopyButtons(root) {
        var scope = root || document;
        if (!scope.querySelectorAll) return;
        Array.prototype.forEach.call(scope.querySelectorAll("pre"), function (pre) {
            if (pre.getAttribute("data-copy") === "1") return;
            pre.setAttribute("data-copy", "1");
            var wrap = document.createElement("div");
            wrap.className = "code-wrap";
            pre.parentNode.insertBefore(wrap, pre);
            wrap.appendChild(pre);
            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "code-copy";
            btn.textContent = "Copiar";
            btn.addEventListener("click", function () {
                copyText(pre.textContent || "");
                var prev = btn.textContent;
                btn.textContent = "Copiado!";
                setTimeout(function () { btn.textContent = prev; }, 1500);
            });
            wrap.appendChild(btn);
        });
    }

    function md(s) {
        if (!s) return "";
        if (typeof marked !== "undefined" && marked.parse) {
            try {
              var parsed = marked.parse(s);
              var parser = new DOMParser();
              var doc = parser.parseFromString(parsed, "text/html");
              var parserDom = doc.querySelector("body");
              parserDom.querySelectorAll("h1[id], h2[id], h3[id], h4[id]").forEach(function (header) {
                var anchor = header.getAttribute("id");
                header.setAttribute("id", "presentation-" + anchor);
                header.className = "group text-xl font-bold text-gray-900 mb-1";
                if (anchor) {
                  var link = document.createElement("a");
                  link.href = "#presentation-" + anchor;
                  link.className = "ml-2 text-gray-300 opacity-0 group-hover:opacity-100 transition-opacity no-underline";
                  link.textContent = "#";
                  header.appendChild(link);
                }
              });
              return parserDom.innerHTML || "";
            } catch (e) {  }
        }
        return "<p>" + esc(s) + "</p>";
    }

    function methodBadge(m, extra) {
        var c = METHOD_COLOR[m] || "gray";
        var text = (m === "GET" || m === "PUT") ? "text-" + c + "-900" : "text-white";
        return '<span class="inline-block ' + (extra || "") + ' px-2 py-1 rounded ' +
            'text-xs font-bold tracking-wide ' + text + ' bg-' + c + '-500">' + esc(m) + "</span>";
    }

    // Realce simples de JSON
    function highlightJSON(value) {
        var json;
        if (typeof value === "string") {
            try { json = JSON.stringify(JSON.parse(value), null, 2); }
            catch (e) { return '<pre class="code">' + esc(value) + "</pre>"; }
        } else {
            json = JSON.stringify(value, null, 2);
        }
        var html = esc(json)
            .replace(/&quot;([^&]*?)&quot;(\s*:)/g, '<span class="tok-key">&quot;$1&quot;</span>$2')
            .replace(/:\s*&quot;([^&]*?)&quot;/g, ': <span class="tok-str">&quot;$1&quot;</span>')
            .replace(/\b(true|false)\b/g, '<span class="tok-bool">$1</span>')
            .replace(/\bnull\b/g, '<span class="tok-null">null</span>')
            .replace(/(:\s*)(-?\d+(?:\.\d+)?)/g, '$1<span class="tok-num">$2</span>');
        return '<pre class="code">' + html + "</pre>";
    }

    function sectionTitle(t) {
        return '<h4 class="text-xs font-semibold uppercase tracking-wide text-gray-500 mt-5 mb-2">' + esc(t) + "</h4>";
    }

    function renderParams(params) {
        if (!params || !params.length) return "";
        var rows = params.map(function (p) {
            var sch = p.schema || {};
            var type = sch.type || "string";
            if (sch.enum) type += " (" + sch.enum.map(esc).join(" | ") + ")";
            return '<tr class="border-t border-gray-100">' +
                '<td class="py-2 pr-3 font-mono text-sm text-gray-900">' + esc(p.name) + "</td>" +
                '<td class="py-2 pr-3"><span class="text-xs px-2 py-1 rounded bg-gray-100 text-gray-600">' + esc(p["in"]) + "</span></td>" +
                '<td class="py-2 pr-3 text-sm text-gray-600">' + esc(type) + "</td>" +
                '<td class="py-2 pr-3 text-sm">' + (p.required ? '<span class="text-red-600 font-medium">sim</span>' : '<span class="text-gray-400">não</span>') + "</td>" +
                '<td class="py-2 text-sm text-gray-600">' + esc(p.description || "") + "</td>" +
                "</tr>";
        }).join("");
        return sectionTitle("Parâmetros") +
            '<div class="overflow-x-auto"><table class="w-full text-left">' +
            '<thead><tr class="text-xs uppercase text-gray-400">' +
            "<th class='py-1 pr-3 font-medium'>Nome</th><th class='py-1 pr-3 font-medium'>Em</th>" +
            "<th class='py-1 pr-3 font-medium'>Tipo</th><th class='py-1 pr-3 font-medium'>Obrigatório</th>" +
            "<th class='py-1 font-medium'>Descrição</th></tr></thead><tbody>" + rows + "</tbody></table></div>";
    }

    function renderSchemaProps(schema) {
        if (!schema || schema.type !== "object" || !schema.properties) return "";
        var req = schema.required || [];
        var rows = Object.keys(schema.properties).map(function (name) {
            var pr = schema.properties[name] || {};
            var type = pr.type || "string";
            if (pr.enum) type += " (" + pr.enum.map(esc).join(" | ") + ")";
            return '<tr class="border-t border-gray-100">' +
                '<td class="py-2 pr-3 font-mono text-sm text-gray-900">' + esc(name) + "</td>" +
                '<td class="py-2 pr-3 text-sm text-gray-600">' + esc(type) + "</td>" +
                '<td class="py-2 pr-3 text-sm">' + (req.indexOf(name) >= 0 ? '<span class="text-red-600 font-medium">sim</span>' : '<span class="text-gray-400">não</span>') + "</td>" +
                '<td class="py-2 text-sm text-gray-600">' + esc(pr.description || "") + "</td>" +
                "</tr>";
        }).join("");
        return '<div class="overflow-x-auto"><table class="w-full text-left">' +
            '<thead><tr class="text-xs uppercase text-gray-400">' +
            "<th class='py-1 pr-3 font-medium'>Campo</th><th class='py-1 pr-3 font-medium'>Tipo</th>" +
            "<th class='py-1 pr-3 font-medium'>Obrigatório</th><th class='py-1 font-medium'>Descrição</th>" +
            "</tr></thead><tbody>" + rows + "</tbody></table></div>";
    }

    function renderRequestBody(rb) {
        if (!rb || !rb.content) return "";
        var ct = Object.keys(rb.content)[0];
        var media = rb.content[ct] || {};
        var out = sectionTitle("Corpo da requisição") +
            '<p class="text-xs text-gray-500 mb-2">Content-Type: <code class="bg-gray-100 px-1 rounded">' + esc(ct) + "</code>" +
            (rb.required ? ' · <span class="text-red-600">obrigatório</span>' : "") + "</p>";
        out += renderSchemaProps(media.schema);
        if (media.example !== undefined) {
            out += '<p class="text-xs text-gray-500 mt-3 mb-1">Exemplo</p>' + highlightJSON(media.example);
        }
        if (media.examples) {
            Object.keys(media.examples).forEach(function (k) {
                var ex = media.examples[k];
                out += '<p class="text-xs text-gray-500 mt-3 mb-1">Exemplo — ' + esc(ex.summary || k) + "</p>" +
                    highlightJSON(ex.value);
            });
        }
        return out;
    }

    function statusColor(code) {
        var n = parseInt(code, 10);
        if (n >= 200 && n < 300) return "green";
        if (n >= 300 && n < 400) return "blue";
        if (n >= 400 && n < 500) return "yellow";
        return "red";
    }

    function renderResponses(responses) {
        if (!responses) return "";
        var out = sectionTitle("Respostas");
        Object.keys(responses).forEach(function (code) {
            var r = responses[code] || {};
            var c = statusColor(code);
            out += '<div class="mt-3">' +
                '<div class="flex items-center gap-2">' +
                '<span class="inline-block px-2 py-1 rounded text-xs font-bold text-white bg-' + c + '-500">' + esc(code) + "</span>" +
                '<span class="text-sm text-gray-600">' + esc(r.description || "") + "</span></div>";
            if (r.content) {
                var ct = Object.keys(r.content)[0];
                var media = r.content[ct] || {};
                if (media.example !== undefined) out += '<div class="mt-2">' + highlightJSON(media.example) + "</div>";
            }
            out += "</div>";
        });
        return out;
    }

    function renderCurl(op) {
        if (!op["x-codeSamples"] || !op["x-codeSamples"].length) return "";
        var src = op["x-codeSamples"][0].source.replace(DEFAULTS.apiHost, CUSTOM.apiHost) || "";
        return sectionTitle("Exemplo (cURL)") + '<pre class="code">' + esc(src) + "</pre>";
    }

    function renderOperationShell(path, method, op) {
        var id = op.operationId || (method + "_" + path);
        OPS[id] = { path: path, method: method, op: op };
        var m = method.toUpperCase();
        return '<details id="' + esc(id) + '" class="op group bg-white border border-gray-200 rounded-lg mb-3 overflow-hidden" ' +
            'data-built="0" data-search="' + esc((m + " " + path + " " + (op.summary || "")).toLowerCase()) + '">' +
            '<summary class="flex items-center gap-3 px-4 py-3 cursor-pointer select-none hover:bg-gray-50">' +
            methodBadge(m, "flex-shrink-0") +
            '<span class="font-mono text-sm text-gray-800 break-all">' + esc(path) + "</span>" +
            '<span class="ml-auto text-sm text-gray-500 hidden md:inline truncate">' + esc(op.summary || "") + "</span>" +
            "</summary>" +
            '<div class="op-body px-4 pb-4 pt-1 border-t border-gray-100"></div>' +
            "</details>";
    }

    function buildOpBody(rec) {
        var op = rec.op;
        var desc = op.description ? md(op.description) : "";
        return (desc ? '<div class="md text-sm text-gray-600 mb-2">' + desc + "</div>" : "") +
            renderParams(op.parameters) +
            renderRequestBody(op.requestBody) +
            renderResponses(op.responses) +
            renderCurl(op) +
            renderTestPanel(rec);
    }

    function ensureBuilt(details) {
        if (!details || details.getAttribute("data-built") === "1") return;
        var rec = OPS[details.id];
        if (!rec) return;
        var bodyEl = details.querySelector(".op-body");
        if (bodyEl) bodyEl.innerHTML = buildOpBody(rec);
        wireOpTest(details, rec);
        addCopyButtons(bodyEl);
        details.setAttribute("data-built", "1");
    }

    function build(spec) {
        // Apresentação
        var pres = document.getElementById("presentation");
        pres.innerHTML = spec.info && spec.info.description ? md(spec.info.description) : "";

        // Agrupa operações por tag, preservando a ordem das tags e dos paths
        var tagOrder = (spec.tags || []).map(function (t) { return t.name; });
        var groups = {};
        tagOrder.forEach(function (t) { groups[t] = []; });

        Object.keys(spec.paths).forEach(function (path) {
            var methods = spec.paths[path];
            Object.keys(methods).forEach(function (method) {
                var op = methods[method];
                var tag = (op.tags && op.tags[0]) || "Outros";
                if (!groups[tag]) { groups[tag] = []; tagOrder.push(tag); }
                groups[tag].push({ path: path, method: method, op: op });
            });
        });

        var nav = document.getElementById("nav");
        var content = document.getElementById("content");
        var navHTML = '<a href="#presentation" class="block px-2 py-1 rounded font-medium text-gray-700 hover:bg-gray-100">📖 Apresentação</a>';
        var contentHTML = "";

        tagOrder.forEach(function (tag) {
            var ops = groups[tag];
            if (!ops || !ops.length) return;
            var anchor = "tag-" + tag.replace(/[^a-zA-Z0-9]+/g, "-").toLowerCase();

            navHTML += '<div class="nav-group mt-3">' +
                '<div class="px-2 py-1 text-xs font-semibold uppercase tracking-wide text-gray-400">' + esc(tag) +
                ' <span class="text-gray-300">(' + ops.length + ")</span></div>";
            ops.forEach(function (o) {
                var id = o.op.operationId;
                var m = o.method.toUpperCase();
                var label = o.op.summary || o.path;
                navHTML += '<a href="#' + esc(id) + '" data-target="' + esc(id) + '" ' +
                    'title="' + esc(m + " " + o.path) + '" ' +
                    'data-search="' + esc((m + " " + o.path + " " + (o.op.summary || "")).toLowerCase()) + '" ' +
                    'class="nav-item flex items-center gap-2 px-2 py-1 rounded hover:bg-gray-100">' +
                    methodBadge(m, "w-14 text-center flex-shrink-0") +
                    '<span class="truncate text-gray-600 text-xs">' + esc(label) + "</span></a>";
            });
            navHTML += "</div>";

            contentHTML += '<section class="tag-section mb-10">' +
              '<h2 id="' + anchor + '" class="group text-xl font-bold text-gray-900 mb-1">' + esc(tag) + '<a href="#' + anchor + '" class="ml-2 text-gray-300 opacity-0 group-hover:opacity-100 transition-opacity no-underline">#</a></h2>';
          var tagDef = (spec.tags || []).filter(function (t) {
            return t.name === tag;
          })[0];
            if (tagDef && tagDef.description) {
                contentHTML += '<p class="text-sm text-gray-500 mb-4">' + esc(tagDef.description) + "</p>";
            }
            ops.forEach(function (o) {
                contentHTML += renderOperationShell(o.path, o.method, o.op);
            });
            contentHTML += "</section>";
        });

        nav.innerHTML = navHTML;
        content.innerHTML = contentHTML;

        wireLazyBodies();
        wireSidebar();
        wireSearch();
        wireToolbar();
        openFromHash();
        replaceLinkToDocOnPresentation();
        addCopyButtons(document);
      var loader = document.getElementById("loader");
        if (loader) loader.remove();
    }

    function wireLazyBodies() {
        document.querySelectorAll("details.op").forEach(function (d) {
            d.addEventListener("toggle", function () {
                if (d.open) ensureBuilt(d);
            });
        });
    }

    function openFromHash() {
        var id = (location.hash || "").replace("#", "");
        if (!id) return;
        var el = document.getElementById(id);
        if (el && el.tagName === "DETAILS") {
            ensureBuilt(el);
            el.open = true;
            el.scrollIntoView();
        }
    }

    function wireSidebar() {
        document.querySelectorAll("#nav a[data-target]").forEach(function (a) {
            a.addEventListener("click", function () {
                var el = document.getElementById(a.getAttribute("data-target"));
                if (el) { ensureBuilt(el); el.open = true; }
            });
        });
    }

    function wireToolbar() {
        var ex = document.getElementById("expandAll");
        var co = document.getElementById("collapseAll");
        if (ex) ex.addEventListener("click", function () {
            document.querySelectorAll("details.op").forEach(function (d) { ensureBuilt(d); d.open = true; });
        });
        if (co) co.addEventListener("click", function () {
            document.querySelectorAll("details.op").forEach(function (d) { d.open = false; });
        });
    }

    function wireSearch() {
        var input = document.getElementById("search");
        if (!input) return;
        input.addEventListener("input", function () {
            var q = input.value.trim().toLowerCase();

            // filtra itens da sidebar
            document.querySelectorAll("#nav .nav-item").forEach(function (a) {
                var hit = !q || a.getAttribute("data-search").indexOf(q) >= 0;
                a.style.display = hit ? "" : "none";
            });
            document.querySelectorAll("#nav .nav-group").forEach(function (g) {
                var anyVisible = Array.prototype.some.call(
                    g.querySelectorAll(".nav-item"),
                    function (a) { return a.style.display !== "none"; });
                g.style.display = anyVisible ? "" : "none";
            });

            // filtra operações no conteúdo
            document.querySelectorAll("details.op").forEach(function (d) {
                var hit = !q || d.getAttribute("data-search").indexOf(q) >= 0;
                d.style.display = hit ? "" : "none";
            });
            document.querySelectorAll(".tag-section").forEach(function (s) {
                var anyVisible = Array.prototype.some.call(
                    s.querySelectorAll("details.op"),
                    function (d) { return d.style.display !== "none"; });
                s.style.display = anyVisible ? "" : "none";
            });
        });
    }

    function fail(e) {
        var msg = e.message || String(e);
        var loader = document.getElementById("loader");
        if (loader) loader.remove();
        document.getElementById("content").innerHTML =
            '<div class="bg-red-50 border border-red-200 text-red-700 rounded-lg p-6">' +
            "<h2 class='font-bold text-lg mb-2'>Não foi possível carregar a especificação</h2>" +
            "<p class='text-sm'>" + esc(msg) + "</p>" +
            "<p class='text-sm mt-2'>Sirva esta página por HTTP (não <code>file://</code>): o navegador bloqueia o " +
            "carregamento de <code>" + esc(SPEC_URL) + "</code> via protocolo de arquivo.</p></div>";
        console.error(e);
    }

    function customizeSpec(spec) {
        var s = JSON.stringify(spec);
        var apiBase = (CUSTOM.apiUrl || "").replace(/\/+$/, "").replace(/\/v1$/, "");
        var apiV1 = apiBase + "/v1";
        if (apiBase) {
            s = s.split(DEFAULTS.apiUrl).join(apiV1);
            s = s.split("https://" + DEFAULTS.apiHost).join(apiBase);
        }
        if (CUSTOM.panelUrl) {
            s = s.split(DEFAULTS.panelUrl).join(CUSTOM.panelUrl);
        }
        if (CUSTOM.appName) {
            s = s.split(DEFAULTS.appName).join(CUSTOM.appName);
        }
        return JSON.parse(s);
    }

    function applyBranding() {
        if (CUSTOM.appName) {
            document.title = CUSTOM.appName + " Admin API — Documentação";
            var metaDesc = document.querySelector('meta[name="description"]');
            if (metaDesc) {
             metaDesc.setAttribute("content", metaDesc.getAttribute('content').replace(DEFAULTS.appName, CUSTOM.appName));
            }
            var bn = document.getElementById("brandName");
              if (bn) bn.textContent = CUSTOM.appName + " Admin API";
        }
        if (CUSTOM.logo) {
            var dot = document.getElementById("brandLogo");
            if (dot) {
                var box = document.createElement("span");
                box.id = "brandLogo";
                box.className = "inline-flex items-center justify-center bg-white rounded-lg";
                box.style.width = "34px";
                box.style.height = "34px";
                box.style.padding = "4px";
                var img = document.createElement("img");
                img.src = CUSTOM.logo;
                img.alt = CUSTOM.appName || "logo";
                img.style.maxWidth = "100%";
                img.style.maxHeight = "100%";
                img.style.width = "auto";
                img.style.height = "auto";
                box.appendChild(img);
                dot.parentNode.replaceChild(box, dot);
            }
        }
        if (CUSTOM.favicon) {
            var link = document.querySelector("link[rel~='icon']");
            if (!link) {
                link = document.createElement("link");
                link.rel = "icon";
                document.head.appendChild(link);
            }
            link.href = CUSTOM.favicon;
        }
        if (CUSTOM.panelUrl) {
            var pl = document.getElementById("keyPanelLink");
            if (pl) pl.setAttribute("href", CUSTOM.panelUrl + "/settings#tb_api");
        }
        applyColor(CUSTOM.color);
        baseLabelAuthReplace();
        updateImportModalUI();
    }

    // A cor do tenant é aplicada SOMENTE no cabeçalho (barra superior).
    // Clareia uma cor hex misturando com branco (amt entre 0 e 1).
    function lighten(hex, amt) {
        hex = String(hex).replace("#", "");
        if (hex.length === 3) { hex = hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2]; }
        if (hex.length < 6) return "#" + hex;
        var r = parseInt(hex.substr(0, 2), 16);
        var g = parseInt(hex.substr(2, 2), 16);
        var b = parseInt(hex.substr(4, 2), 16);
        r = Math.round(r + (255 - r) * amt);
        g = Math.round(g + (255 - g) * amt);
        b = Math.round(b + (255 - b) * amt);
        return "rgb(" + r + "," + g + "," + b + ")";
    }

    // A cor do tenant é aplicada SOMENTE no cabeçalho (barra superior).
    function applyColor(color) {
        if (!color) return;
        var header = document.querySelector("header");
        if (header) {
            header.style.backgroundColor = color;
            header.style.borderColor = color;
        }
        // Botões "Testar API" e "Importar documentação": fundo um pouco mais
        // claro que a cor, e hover na própria cor.
        var lighter = lighten(color, 0.18);
        var css =
            "#authBtn,#importBtn{background-color:" + lighter + " !important;" +
            "border-color:" + lighter + " !important;color:#fff !important;}" +
            "#authBtn:hover,#importBtn:hover{background-color:" + color + " !important;" +
            "border-color:" + color + " !important;}";
        var st = document.createElement("style");
        st.textContent = css;
        document.head.appendChild(st);
    }

    function jwtSamples() {
        var php =
"<?php\n" +
"// composer require firebase/php-jwt\n" +
"use Firebase\\JWT\\JWT;\n" +
"\n" +
"// 1) Chave privada do ambiente (Painel de Controle > Configuracoes > API), em base64\n" +
"$base64Key = 'SUA_CHAVE_PRIVADA_BASE64';\n" +
"$secret = base64_decode($base64Key); // chave decodificada\n" +
"\n" +
"// 2) jti retornado por POST /auth/login\n" +
"$jti = 'JTI_DO_LOGIN';\n" +
"\n" +
"// 3) Gera o JWT (HS256, header { \"typ\": \"JWT\", \"alg\": \"HS256\" })\n" +
"$token = JWT::encode(['jti' => $jti], $secret, 'HS256');\n" +
"\n" +
"echo $token . PHP_EOL;\n" +
"// Envie em cada requisicao: Authorization: bearer <token>";

        var js =
"// npm install jsonwebtoken\n" +
"const jwt = require('jsonwebtoken');\n" +
"\n" +
"// 1) Chave privada do ambiente (base64) -> decodificada\n" +
"const base64Key = 'SUA_CHAVE_PRIVADA_BASE64';\n" +
"const secret = Buffer.from(base64Key, 'base64');\n" +
"\n" +
"// 2) jti retornado por POST /auth/login\n" +
"const jti = 'JTI_DO_LOGIN';\n" +
"\n" +
"// 3) Gera o JWT (HS256)\n" +
"const token = jwt.sign({ jti: jti }, secret, { algorithm: 'HS256' });\n" +
"\n" +
"console.log(token);\n" +
"// Authorization: bearer <token>";

        var ts =
"// npm install jose\n" +
"import { SignJWT } from 'jose';\n" +
"\n" +
"// 1) Chave privada do ambiente (base64) -> bytes decodificados\n" +
"const base64Key = 'SUA_CHAVE_PRIVADA_BASE64';\n" +
"const secret = Uint8Array.from(atob(base64Key), (c) => c.charCodeAt(0));\n" +
"\n" +
"// 2) jti retornado por POST /auth/login\n" +
"const jti = 'JTI_DO_LOGIN';\n" +
"\n" +
"// 3) Gera o JWT (HS256, header { alg: 'HS256', typ: 'JWT' })\n" +
"const token = await new SignJWT({ jti })\n" +
"  .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })\n" +
"  .sign(secret);\n" +
"\n" +
"console.log(token);\n" +
"// Authorization: bearer <token>";

        var py =
"# pip install pyjwt\n" +
"import base64\n" +
"import jwt  # PyJWT\n" +
"\n" +
"# 1) Chave privada do ambiente (base64) -> decodificada\n" +
"base64_key = \"SUA_CHAVE_PRIVADA_BASE64\"\n" +
"secret = base64.b64decode(base64_key)\n" +
"\n" +
"# 2) jti retornado por POST /auth/login\n" +
"jti = \"JTI_DO_LOGIN\"\n" +
"\n" +
"# 3) Gera o JWT (HS256)\n" +
"token = jwt.encode({\"jti\": jti}, secret, algorithm=\"HS256\")\n" +
"\n" +
"print(token)\n" +
"# Authorization: bearer <token>";

        var java =
"// io.jsonwebtoken:jjwt-api / jjwt-impl / jjwt-jackson (0.11+)\n" +
"import io.jsonwebtoken.Jwts;\n" +
"import io.jsonwebtoken.SignatureAlgorithm;\n" +
"import javax.crypto.spec.SecretKeySpec;\n" +
"import java.security.Key;\n" +
"import java.util.Base64;\n" +
"\n" +
"// 1) Chave privada do ambiente (base64) -> decodificada\n" +
"String base64Key = \"SUA_CHAVE_PRIVADA_BASE64\";\n" +
"byte[] secret = Base64.getDecoder().decode(base64Key);\n" +
"Key key = new SecretKeySpec(secret, SignatureAlgorithm.HS256.getJcaName());\n" +
"\n" +
"// 2) jti retornado por POST /auth/login\n" +
"String jti = \"JTI_DO_LOGIN\";\n" +
"\n" +
"// 3) Gera o JWT (HS256, header { \"typ\": \"JWT\", \"alg\": \"HS256\" })\n" +
"String token = Jwts.builder()\n" +
"        .setHeaderParam(\"typ\", \"JWT\")\n" +
"        .claim(\"jti\", jti)\n" +
"        .signWith(key, SignatureAlgorithm.HS256)\n" +
"        .compact();\n" +
"\n" +
"System.out.println(token);\n" +
"// Authorization: bearer <token>";

        var go =
"// go get github.com/golang-jwt/jwt/v5\n" +
"package main\n" +
"\n" +
"import (\n" +
"\t\"encoding/base64\"\n" +
"\t\"fmt\"\n" +
"\n" +
"\t\"github.com/golang-jwt/jwt/v5\"\n" +
")\n" +
"\n" +
"func main() {\n" +
"\t// 1) Chave privada do ambiente (base64) -> decodificada\n" +
"\tbase64Key := \"SUA_CHAVE_PRIVADA_BASE64\"\n" +
"\tsecret, _ := base64.StdEncoding.DecodeString(base64Key)\n" +
"\n" +
"\t// 2) jti retornado por POST /auth/login\n" +
"\tjti := \"JTI_DO_LOGIN\"\n" +
"\n" +
"\t// 3) Gera o JWT (HS256)\n" +
"\ttoken := jwt.NewWithClaims(jwt.SigningMethodHS256, jwt.MapClaims{\n" +
"\t\t\"jti\": jti,\n" +
"\t})\n" +
"\n" +
"\tsigned, err := token.SignedString(secret)\n" +
"\tif err != nil {\n" +
"\t\tpanic(err)\n" +
"\t}\n" +
"\n" +
"\tfmt.Println(signed)\n" +
"\t// Authorization: bearer <signed>\n" +
"}";

        return [
            ["PHP", php], ["JavaScript", js], ["TypeScript", ts],
            ["Python", py], ["Java", java], ["Go", go]
        ];
    }

    function jwtTabClass(active) {
        return "jwt-tab px-3 py-1 rounded text-sm font-medium " +
            (active ? "bg-blue-600 text-white" : "bg-gray-100 text-gray-600 hover:bg-gray-200");
    }

    function jwtSectionHTML() {
        var samples = jwtSamples();
        var tabs = samples.map(function (s, i) {
            return '<button type="button" class="' + jwtTabClass(i === 0) + '" data-lang="' + s[0] + '">' + s[0] + "</button>";
        }).join("");
        var panes = samples.map(function (s, i) {
            return '<pre class="code jwt-pane" data-lang="' + s[0] + '"' +
                (i === 0 ? "" : ' style="display:none"') + ">" + esc(s[1]) + "</pre>";
        }).join("");
        return '<p class="text-sm text-gray-600 mb-4">A API usa <strong>JWT</strong> (assinatura ' +
            "<strong>HS256</strong>). Para gerar o token: <strong>(1)</strong> decodifique de " +
            "<strong>base64</strong> a chave privada do ambiente (Painel de Controle &rarr; Configurações &rarr; API); " +
            "<strong>(2)</strong> obtenha o <code>jti</code> via <code>POST /auth/login</code>; " +
            "<strong>(3)</strong> assine o payload <code>{ \"jti\": \"...\" }</code> com header " +
            "<code>{ \"typ\": \"JWT\", \"alg\": \"HS256\" }</code>; <strong>(4)</strong> envie em cada " +
            "requisição no header <code>Authorization: bearer &lt;jwt&gt;</code>.</p>" +
            '<div class="flex flex-wrap gap-1 mb-3">' + tabs + "</div>" + panes;
    }

    function wireJwt(container) {
        if (!container) return;
        var tabs = container.querySelectorAll(".jwt-tab");
        var panes = container.querySelectorAll(".jwt-pane");
        tabs.forEach(function (t) {
            t.addEventListener("click", function () {
                var lang = t.getAttribute("data-lang");
                tabs.forEach(function (x) { x.className = jwtTabClass(x === t); });
                panes.forEach(function (p) {
                    p.style.display = (p.getAttribute("data-lang") === lang) ? "" : "none";
                });
            });
        });
    }

    function openKeyModal() {
        var a = document.getElementById("authModal"); if (a) a.classList.add("hidden");
        var i = document.getElementById("importModal"); if (i) i.classList.add("hidden");
        var m = document.getElementById("keyModal"); if (m) m.classList.remove("hidden");
        setModalHash("key");
    }
    function closeKeyModal() {
        var m = document.getElementById("keyModal"); if (m) m.classList.add("hidden");
        clearModalHash("key");
    }

  function replaceLinkToDocOnPresentation() {
    var presentation = document.getElementById("presentation");
    var targetHTML = '<p>Visite a documentação em <a href="https://' + CUSTOM.apiHost + '/doc.html">' + CUSTOM.appName + ' Admin API</a></p>';
    presentation.innerHTML = presentation.innerHTML.replace(targetHTML, '<div id="jwt-__-import"></div>');
    var jwtImportArea = presentation.querySelector('#jwt-__-import');
    if (jwtImportArea) {
      var jwtSec = document.getElementById("jwt-section");
      if (jwtSec) {
        jwtImportArea.innerHTML += jwtSec.innerHTML;
        jwtSec.remove();
      }
      var importSec = document.getElementById("import-section");
      if (importSec) {
        jwtImportArea.innerHTML += importSec.innerHTML;
        importSec.remove();
      }
    }
    bind("openKeyFromSection", "click", openKeyModal);
    wireImport(presentation)
  }

  function initKey() {
        var keySec = document.getElementById("keyJwtSection");
        if (keySec) { keySec.innerHTML = jwtSectionHTML(); wireJwt(keySec); addCopyButtons(keySec); }

        var sec = document.getElementById("jwt-section");
        if (sec) {
            sec.innerHTML =
                '<h2 id="presentation-authentication" class="group text-xl font-bold text-gray-900 mb-1">Autenticação<a href="#presentation-authentication" class="ml-2 text-gray-300 opacity-0 group-hover:opacity-100 transition-opacity no-underline">#</a></h2>' +
                '<p class="text-sm text-gray-500 mb-3">Para se autenticar na API siga as instruções no link abaixo e verifique também exemplos de códigos para gerar o token pra sua aplicação</p>' +
                '<button id="openKeyFromSection" type="button" class="px-3 py-2 rounded bg-blue-600 hover:bg-blue-500 text-white text-sm font-semibold">🔑 Obter chave / gerar token JWT</button>';
        }

        var modal = document.getElementById("keyModal");
        bind("keyBtn", "click", openKeyModal);
        bind("keyClose", "click", closeKeyModal);
        bind("openKeyFromAuth", "click", openKeyModal);
        bind("openKeyFromSection", "click", openKeyModal);
        if (modal) modal.addEventListener("click", function (e) { if (e.target === modal) closeKeyModal(); });
        document.addEventListener("keydown", function (e) { if (e.key === "Escape") closeKeyModal(); });
    }

    function importInstructionsHTML() {
        function card(name, steps) {
            return '<div class="border border-gray-200 rounded-lg p-4">' +
                '<h4 class="font-semibold text-gray-900 mb-2">' + name + "</h4>" +
                '<ol class="list-decimal pl-4 text-sm text-gray-600 space-y-1">' +
                steps.map(function (s) { return "<li>" + s + "</li>"; }).join("") +
                "</ol></div>";
        }
        return '<p class="text-sm text-gray-600 mb-4">Esta documentação segue o padrão ' +
            "<strong>OpenAPI 3.0</strong> e pode ser importada em qualquer cliente de API — " +
            "por <strong>arquivo</strong> (baixe o YAML/JSON) ou pela <strong>URL</strong> da especificação:</p>" +
            '<div class="flex gap-2 mb-5">' +
            '<input type="text" readonly class="spec-url-input flex-1 px-3 py-2 text-sm rounded border border-gray-300 bg-gray-50 font-mono" value=""/>' +
            '<button type="button" class="copy-url-btn px-3 py-2 rounded bg-gray-100 hover:bg-gray-200 text-sm whitespace-nowrap">Copiar</button>' +
            "</div>" +
            '<div class="import-grid">' +
            card("Postman", [
                "Clique em <strong>Import</strong> (canto superior esquerdo).",
                "Solte o arquivo <code>openapi.yaml</code>/<code>.json</code> ou cole a URL acima.",
                "Confirme — uma <em>Collection</em> é gerada a partir do OpenAPI."
            ]) +
            card("Insomnia", [
                "Menu <strong>Create → Import From</strong>.",
                "Escolha <strong>File</strong> (YAML/JSON) ou <strong>URL</strong>.",
                "Importe como <em>Document</em> ou <em>Collection</em>."
            ]) +
            card("Swagger Editor / UI", [
                "Acesse <strong>editor.swagger.io</strong>.",
                "Menu <strong>File → Import file</strong> (ou <em>Import URL</em>).",
                "O spec é validado e visualizado na hora."
            ]) +
            card("Apidog", [
                "Novo projeto → <strong>Import</strong>.",
                "Selecione <strong>OpenAPI/Swagger</strong> e aponte o arquivo ou a URL.",
                "Endpoints e schemas são criados automaticamente."
            ]) +
            "</div>";
    }

    function wireImport(container) {
        if (!container) return;
        var url = CUSTOM.apiUrl + "/doc/openapi.yaml";
        container.querySelectorAll(".spec-url-input").forEach(function (i) { i.value = url; });
        container.querySelectorAll(".copy-url-btn").forEach(function (b) {
            b.addEventListener("click", function () {
                var input = b.parentNode.querySelector(".spec-url-input");
                var done = false;
                try {
                    if (navigator.clipboard) { navigator.clipboard.writeText(url); done = true; }
                } catch (e) { /* fallback */ }
                if (!done && input) {
                    input.select();
                    try { document.execCommand("copy"); } catch (e) { /* ignore */ }
                }
                var prev = b.textContent;
                b.textContent = "Copiado!";
                setTimeout(function () { b.textContent = prev; }, 1500);
            });
        });
    }

    function initImport() {
        var section = document.getElementById("import-section");
        if (section) {
            section.innerHTML =
                '<h2 id="presentation-import-doc" class="group text-xl font-bold text-gray-900 mb-1">Importar a documentação<a href="#presentation-import-doc" class="ml-2 text-gray-300 opacity-0 group-hover:opacity-100 transition-opacity no-underline">#</a></h2>' +
                '<p class="text-sm text-gray-500 mb-4">Use a especificação OpenAPI 3 na sua ferramenta favorita.</p>' +
                '<div class="bg-white border border-gray-200 rounded-lg p-5">' + importInstructionsHTML() + "</div>";
            wireImport(section);
        }
        var body = document.getElementById("importBody");
        if (body) { body.innerHTML = importInstructionsHTML(); wireImport(body); }

        var modal = document.getElementById("importModal");
        var open = function () { if (modal) modal.classList.remove("hidden"); setModalHash("import"); };
        var close = function () { if (modal) modal.classList.add("hidden"); clearModalHash("import"); };
        var btn = document.getElementById("importBtn");
        if (btn) btn.addEventListener("click", open);
        var foot = document.getElementById("importFooter");
        if (foot) foot.addEventListener("click", open);
        var x = document.getElementById("importClose");
        if (x) x.addEventListener("click", close);
        if (modal) {
            modal.addEventListener("click", function (e) { if (e.target === modal) close(); });
        }
        document.addEventListener("keydown", function (e) { if (e.key === "Escape") close(); });
    }

    var AUTH = { jwt: null, jti: null };
    var AUTH_STORE = "skymailDocsAuth";

    function baseUrl() {
      return location.origin + "/v1";
    }

    function isAuthed() { return !!AUTH.jwt; }

    function loadAuth() {
        try {
            var raw = sessionStorage.getItem(AUTH_STORE);
            if (raw) { var o = JSON.parse(raw); AUTH.jwt = o.jwt || null; AUTH.jti = o.jti || null; }
        } catch (e) { /* ignore */ }
    }

    function saveAuth() {
        try { sessionStorage.setItem(AUTH_STORE, JSON.stringify(AUTH)); } catch (e) { /* ignore */ }
    }

    function clearAuth() {
        AUTH.jwt = null; AUTH.jti = null;
        try { sessionStorage.removeItem(AUTH_STORE); } catch (e) { /* ignore */ }
        updateAuthUI();
    }

    function updateImportModalUI() {
      var body = document.getElementById("importBody");
      if (body) { wireImport(body) }
    }

    function updateAuthUI() {
        var banner = document.getElementById("authBanner");
        if (banner) banner.classList.toggle("hidden", !isAuthed());
        var status = document.getElementById("authStatus");
        if (status) {
            status.textContent = isAuthed() ? "● Ativo" : "○ Não iníciado";
            status.className = "text-xs font-semibold " + (isAuthed() ? "text-green-300" : "opacity-75");
        }
        baseLabelAuthReplace();
        var res = document.getElementById("authResult");
        var jwtEl = document.getElementById("authJwt");
        if (res && jwtEl) {
            if (isAuthed()) { res.classList.remove("hidden"); jwtEl.value = AUTH.jwt; }
            else { res.classList.add("hidden"); jwtEl.value = ""; }
        }
    }

    // Gera o JWT (HS256) no navegador via Web Crypto (requer contexto seguro/HTTPS).
    function makeJwt(jti, base64Key) {
        function b64urlStr(s) {
            return btoa(unescape(encodeURIComponent(s)))
                .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
        }
        function b64urlBytes(bytes) {
            var bin = "";
            for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
            return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
        }
        var keyBytes = Uint8Array.from(atob(base64Key), function (c) { return c.charCodeAt(0); });
        var header = b64urlStr(JSON.stringify({ typ: "JWT", alg: "HS256" }));
        var payload = b64urlStr(JSON.stringify({ jti: jti }));
        var signingInput = header + "." + payload;
        return crypto.subtle.importKey(
            "raw", keyBytes, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]
        ).then(function (key) {
            return crypto.subtle.sign("HMAC", key, new TextEncoder().encode(signingInput));
        }).then(function (sig) {
            return signingInput + "." + b64urlBytes(new Uint8Array(sig));
        });
    }

    function setMsg(el, text, color) {
        if (!el) return;
        el.className = "text-xs mt-2 text-" + (color || "gray") + "-600";
        el.textContent = text;
    }

    // Obtém o jti chamando POST /auth/login (mesma origem) e preenche o campo jti.
    function doLogin() {
        var userEl = document.getElementById("authLoginUser");
        var passEl = document.getElementById("authLoginPass");
        var captchaInput = document.getElementById("captchaInput");
        var msg = document.getElementById("authLoginMsg");
        var user = userEl ? userEl.value.trim() : "";
        var pass = passEl ? passEl.value : "";
        var captcha = captchaInput ? captchaInput.value.trim() : "";

        if (!user || !pass) { setMsg(msg, "Informe usuário e senha.", "red"); return; }

        var captchaContainer = document.getElementById("captchaContainer");
        if (captchaContainer && captchaContainer.classList.contains("hidden")) {
            refreshCaptcha();
            captchaContainer.classList.remove("hidden");
            setMsg(msg, "Por favor, digite o código de segurança para continuar.", "gray");
            return;
        }

        if (!captcha) {
            setMsg(msg, "Por favor, digite o código de segurança.", "red");
            return;
        }

        var body = new URLSearchParams();
        body.append("username", user);
        body.append("password", pass);
        setMsg(msg, "Autenticando…", "gray");

        var headers = { "Content-Type": "application/x-www-form-urlencoded" };
        if (captcha) {
            headers["X-DOCS-CAPTCHA"] = captcha;
        }

        fetch(baseUrl() + "/auth/login", {
            method: "POST",
            headers: headers,
            body: body.toString()
        }).then(function (r) {
            return r.json().then(function (j) { return { status: r.status, j: j }; })
                .catch(function () { return { status: r.status, j: null }; });
        }).then(function (res) {
            var jti = res.j && res.j.data && res.j.data.jti;
            if (res.status >= 200 && res.status < 300 && jti) {
                var jtiEl = document.getElementById("authJti");
                if (jtiEl) jtiEl.value = jti; // exibe/insere o jti após o login validado pelo captcha
                if (passEl) passEl.value = "";
                if (captchaInput) captchaInput.value = "";
                if (captchaContainer) captchaContainer.classList.add("hidden");
                setMsg(msg, "Login OK ✔ — jti preenchido abaixo. Cole a chave privada e gere o JWT.", "green");
            } else {
                setMsg(msg, (res.j && res.j.message) || ("Falha no login (HTTP " + res.status + ")."), "red");
                refreshCaptcha();
                if (captchaInput) captchaInput.value = "";
            }
        }).catch(function (e) {
            setMsg(msg, "Erro de rede: " + (e.message || e), "red");
            refreshCaptcha();
        });
    }

    function refreshCaptcha() {
        var img = document.getElementById("captchaImg");
        if (img) {
            img.src = baseUrl() + "/doc/captcha?t=" + new Date().getTime();
        }
    }

    function onGenerate() {
        var jtiEl = document.getElementById("authJti");
        var keyEl = document.getElementById("authKey");
        var err = document.getElementById("authError");
        var jti = jtiEl ? jtiEl.value.trim() : "";
        var key = keyEl ? keyEl.value.trim() : "";
        function showErr(m) { if (err) { err.textContent = m; err.classList.remove("hidden"); } }
        if (err) err.classList.add("hidden");
        if (!jti) { return showErr("Faça login acima para obter o jti (ou cole um jti existente)."); }
        if (!key) { return showErr("Informe a chave privada do ambiente (base64)."); }
        if (!(window.crypto && crypto.subtle)) {
            return showErr("A geração de JWT requer HTTPS (Web Crypto indisponível neste contexto).");
        }
        makeJwt(jti, key).then(function (jwt) {
            AUTH.jwt = jwt; AUTH.jti = jti; saveAuth();
            if (keyEl) keyEl.value = ""; // não persiste a chave privada
            updateAuthUI();
        }).catch(function (e) {
            showErr("Falha ao gerar o JWT (verifique a chave base64): " + (e.message || e));
        });
    }

    function baseLabelAuthReplace() {
      var baseLabel = document.getElementById("authBaseLabel");
      if (baseLabel) baseLabel.textContent = CUSTOM.apiUrl;
      var bbase = document.getElementById("authBannerBase");
      if (bbase) bbase.textContent = baseUrl();
    }

    function initAuth() {
        loadAuth();
        baseLabelAuthReplace();
        var modal = document.getElementById("authModal");
        var open = function () { if (modal) modal.classList.remove("hidden"); updateAuthUI(); setModalHash("test"); };
        var close = function () { if (modal) modal.classList.add("hidden"); clearModalHash("test"); };
        bind("authBtn", "click", open);
        bind("authClose", "click", close);
        if (modal) modal.addEventListener("click", function (e) { if (e.target === modal) close(); });
        bind("authLogin", "click", doLogin);
        bind("captchaImg", "click", refreshCaptcha);
        bind("authGenerate", "click", onGenerate);
        bind("authClear", "click", function () {
            var k = document.getElementById("authKey"); if (k) k.value = "";
            var j = document.getElementById("authJti"); if (j) j.value = "";
            clearAuth();
        });
        bind("authBannerClear", "click", clearAuth);
        bind("authCopy", "click", function () {
            var el = document.getElementById("authJwt");
            if (!el) return;
            try { if (navigator.clipboard) navigator.clipboard.writeText(AUTH.jwt || ""); }
            catch (e) { el.select(); try { document.execCommand("copy"); } catch (e2) { /* ignore */ } }
        });
        document.addEventListener("keydown", function (e) { if (e.key === "Escape") close(); });
        updateAuthUI();
    }

    function bind(id, ev, fn) {
        var el = document.getElementById(id);
        if (el) el.addEventListener(ev, fn);
    }

    function setModalHash(name) {
        if (location.hash !== "#" + name) {
            try { history.replaceState(null, "", "#" + name); } catch (e) { /* ignore */ }
        }
    }
    function clearModalHash(name) {
        if (location.hash === "#" + name) {
            try { history.replaceState(null, "", location.pathname + location.search); } catch (e) { /* ignore */ }
        }
    }
    function applyModalHash() {
        var h = (location.hash || "").toLowerCase();
        if (h === "#test" || h === "#test-api") {
            var a = document.getElementById("authModal");
            if (a) { a.classList.remove("hidden"); updateAuthUI(); }
        } else if (h === "#import" || h === "#import") {
            var i = document.getElementById("importModal");
            if (i) { i.classList.remove("hidden"); updateImportModalUI(i); }
        } else if (h === "#key" || h === "#get-key") {
            var k = document.getElementById("keyModal");
            if (k) k.classList.remove("hidden");
        }
    }

    // ---- painel "Testar" por operação ----
    function testField(name, where, example, required) {
        return '<div class="flex items-center gap-2">' +
            '<label class="text-xs font-mono text-gray-600" style="min-width:9.5rem">' + esc(name) +
            (required ? ' <span class="text-red-600">*</span>' : "") +
            ' <span class="text-gray-400">(' + esc(where) + ")</span></label>" +
            '<input type="text" class="test-input flex-1 px-2 py-1 text-xs border border-gray-300 rounded" ' +
            'data-name="' + esc(name) + '" data-in="' + esc(where) + '" value="' +
            esc(example == null ? "" : example) + '"/></div>';
    }

    function renderTestPanel(rec) {
        var op = rec.op;
        var method = rec.method.toUpperCase();
        var fields = "";
        (op.parameters || []).forEach(function (p) {
            var ex = (p.schema && p.schema.example != null) ? p.schema.example : "";
            fields += testField(p.name, p["in"], ex, p.required);
        });
        var bodyHtml = "";
        if (op.requestBody && op.requestBody.content) {
            var ct = Object.keys(op.requestBody.content)[0];
            var media = op.requestBody.content[ct] || {};
            if (ct.indexOf("json") >= 0) {
                var ex = media.example !== undefined ? JSON.stringify(media.example, null, 2) : "{}";
                bodyHtml = '<div class="text-xs font-semibold uppercase text-gray-400 mt-2">Corpo · ' + esc(ct) + "</div>" +
                    '<textarea class="test-body w-full px-2 py-1 text-xs font-mono border border-gray-300 rounded" ' +
                    'data-ctype="' + esc(ct) + '" rows="6">' + esc(ex) + "</textarea>";
            } else {
                var props = (media.schema && media.schema.properties) || {};
                var reqd = (media.schema && media.schema.required) || [];
                bodyHtml = '<div class="text-xs font-semibold uppercase text-gray-400 mt-2">Corpo · ' + esc(ct) + "</div>" +
                    '<input type="hidden" class="test-ctype" value="' + esc(ct) + '"/>';
                Object.keys(props).forEach(function (n) {
                    var pex = props[n].example != null ? props[n].example : "";
                    bodyHtml += testField(n, "form", pex, reqd.indexOf(n) >= 0);
                });
            }
        }
        return '<div class="op-test mt-4 border-t border-gray-100 pt-3">' +
            '<button type="button" class="test-toggle inline-flex items-center gap-1 px-3 py-1 rounded bg-gray-900 text-white text-sm">▶ Testar (produção)</button>' +
            '<div class="test-form hidden mt-3 space-y-2">' +
            (fields ? '<div class="text-xs font-semibold uppercase text-gray-400">Parâmetros</div>' + fields : "") +
            bodyHtml +
            '<div class="flex items-center gap-2 pt-1">' +
            '<button type="button" class="test-exec px-3 py-1 rounded bg-blue-600 text-white text-sm font-semibold">Executar</button>' +
            '<span class="text-xs text-red-600">⚠ chamada real em produção</span></div>' +
            '<div class="test-result"></div>' +
            "</div></div>";
    }

    function wireOpTest(details, rec) {
        var test = details.querySelector(".op-test");
        if (!test) return;
        var toggle = test.querySelector(".test-toggle");
        var form = test.querySelector(".test-form");
        if (toggle && form) toggle.addEventListener("click", function () { form.classList.toggle("hidden"); });
        var exec = test.querySelector(".test-exec");
        if (exec) exec.addEventListener("click", function () { executeTest(test, rec, exec); });
    }

    function executeTest(test, rec, btn) {
        var resultEl = test.querySelector(".test-result");
        if (!isAuthed()) {
            resultEl.innerHTML = '<div class="mt-2 text-sm text-red-600">Configure a autenticação primeiro no botão <strong>🔑 Testar API</strong>.</div>';
            return;
        }
        var method = rec.method.toUpperCase();
        var pathVals = {}, queryVals = {}, formVals = {};
        test.querySelectorAll(".test-input").forEach(function (i) {
            var where = i.getAttribute("data-in");
            var n = i.getAttribute("data-name");
            if (where === "path") pathVals[n] = i.value;
            else if (where === "query") queryVals[n] = i.value;
            else if (where === "form") formVals[n] = i.value;
        });
        var filledPath = rec.path.replace(/\{([^}]+)\}/g, function (m, n) {
            return encodeURIComponent(pathVals[n] != null ? pathVals[n] : "");
        });
        var qs = Object.keys(queryVals)
            .filter(function (k) { return queryVals[k] !== ""; })
            .map(function (k) { return encodeURIComponent(k) + "=" + encodeURIComponent(queryVals[k]); })
            .join("&");
        var url = baseUrl() + filledPath + (qs ? "?" + qs : "");
        var headers = { "Authorization": "bearer " + AUTH.jwt };
        var init = { method: method, headers: headers };
        if (method !== "GET" && method !== "HEAD") {
            var bodyTa = test.querySelector(".test-body");
            if (bodyTa) {
                headers["Content-Type"] = bodyTa.getAttribute("data-ctype") || "application/json";
                init.body = bodyTa.value;
            } else {
                var keys = Object.keys(formVals);
                if (keys.length) {
                    var usp = new URLSearchParams();
                    keys.forEach(function (k) { if (formVals[k] !== "") usp.append(k, formVals[k]); });
                    var ctEl = test.querySelector(".test-ctype");
                    headers["Content-Type"] = (ctEl && ctEl.value) || "application/x-www-form-urlencoded";
                    init.body = usp.toString();
                }
            }
        }
        resultEl.innerHTML = '<div class="mt-2 text-xs text-gray-500">Executando ' + esc(method) + " " + esc(url) + " …</div>";
        if (btn) btn.disabled = true;
        var t0 = Date.now();
        fetch(url, init).then(function (r) {
            return r.text().then(function (text) {
                return { status: r.status, statusText: r.statusText, text: text };
            });
        }).then(function (res) {
            if (btn) btn.disabled = false;
            var ms = Date.now() - t0;
            var c = (res.status >= 200 && res.status < 300) ? "green" : (res.status >= 400 ? "red" : "yellow");
            var bodyHtml;
            try { bodyHtml = highlightJSON(JSON.parse(res.text)); }
            catch (e) { bodyHtml = '<pre class="code">' + esc(res.text || "(sem corpo)") + "</pre>"; }
            resultEl.innerHTML = '<div class="mt-3"><div class="flex items-center gap-2 mb-1">' +
                '<span class="inline-block px-2 py-1 rounded text-xs font-bold text-white bg-' + c + '-500">' + res.status + "</span>" +
                '<span class="text-xs text-gray-500">' + esc(res.statusText) + " · " + ms + " ms</span></div>" + bodyHtml + "</div>";
            addCopyButtons(resultEl);
        }).catch(function (e) {
            if (btn) btn.disabled = false;
            resultEl.innerHTML = '<div class="mt-2 text-sm text-red-600">Erro de rede: ' + esc(e.message || e) + "</div>";
        });
    }

    // Drawer do menu lateral em telas estreitas (< 1024px).
    function initSidebar() {
        var sidebar = document.getElementById("sidebar");
        var backdrop = document.getElementById("sidebarBackdrop");
        function closeSidebar() {
            if (sidebar) sidebar.classList.remove("sidebar-open");
            if (backdrop) backdrop.classList.add("hidden");
        }
        function toggleSidebar() {
            if (sidebar && sidebar.classList.contains("sidebar-open")) {
                closeSidebar();
            } else {
                if (sidebar) sidebar.classList.add("sidebar-open");
                if (backdrop) backdrop.classList.remove("hidden");
            }
        }
        bind("sidebarToggle", "click", toggleSidebar);
        if (backdrop) backdrop.addEventListener("click", closeSidebar);
        var nav = document.getElementById("nav");
        if (nav) nav.addEventListener("click", function (e) { if (e.target.closest && e.target.closest("a")) closeSidebar(); });
        document.addEventListener("keydown", function (e) { if (e.key === "Escape") closeSidebar(); });
    }

    function start() {
        initKey();
        initImport();
        initAuth();
        initSidebar();
      window.addEventListener("hashchange", applyModalHash);
        applyModalHash();
        var specP = fetch(SPEC_URL, { cache: "no-cache" })
            .then(function (r) { if (!r.ok) throw new Error("HTTP " + r.status); return r.json(); });
        var customP = fetch(CUSTOM_URL, { cache: "no-cache" })
            .then(function (r) { return r.ok ? r.json() : { customized: false }; })
            .catch(function () { return { customized: false }; });

        Promise.all([specP, customP])
            .then(function (res) {
                var spec = res[0];
                var c = res[1] || { customized: false };
                if (c && c.customized) {
                    var painelUrl = c.apiUrl.replace('api', 'painel');
                    CUSTOM = {
                      appName: c.appName,
                      apiUrl: c.apiUrl + "/v1",
                      apiHost: c.apiUrl.replace(/^https?:\/\//, ''),
                      panelUrl: painelUrl,
                      panelHost: painelUrl.replace(/^https?:\/\//, ''),
                      logo: c.logo,
                      favicon: c.favicon,
                      color: c.color
                    };
                    spec = customizeSpec(spec);
                    applyBranding();
                }
                build(spec);
            })
            .catch(function (e) { fail(e); });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start);
    } else {
        start();
    }
})();
