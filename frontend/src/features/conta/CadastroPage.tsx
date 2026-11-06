import { useCallback, useEffect, useRef, useState } from "react";
import { useHeadingFocusOnTransition } from "@/shared/hooks/useHeadingFocusOnTransition";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient, useRefreshSession } from "@/app/SessionProvider";
import { Button } from "@/design-system/Button";
import { Checkbox } from "@/design-system/Checkbox";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { FocusedAlert } from "@/design-system/FocusedAlert";
import { FormError } from "@/design-system/FormError";
import { GoogleButton } from "@/design-system/GoogleButton";
import { startGoogleSignin } from "./googleSignin";
import { Link } from "@/design-system/Link";
import { PasswordField } from "@/design-system/PasswordField";
import { RateLimitNotice } from "@/design-system/RateLimitNotice";
import { TextField } from "@/design-system/TextField";
import { ApiError } from "@/shared/api/apiClient";
import { fieldMessage } from "@/shared/messages/messages";
import { useSubmit } from "@/shared/hooks/useSubmit";
import {
  validateDisplayName,
  validateEmail,
  validatePassword,
} from "@/shared/validation/validators";
import "./pageShell.css";

const TERMS_PATH = "/api/v1/legal/terms";
const REGISTER_PATH = "/api/v1/auth/register";
const SUCCESS_MESSAGE = "Conta criada. Bem-vindo(a) ao Deckloom!";
const GENERIC_ERROR = "Não foi possível criar a conta agora. Tente de novo.";

interface TermsInfo {
  version: string;
  termsUrl: string;
  privacyUrl: string;
}

interface FormValues {
  email: string;
  password: string;
  displayName: string;
  acceptTerms: boolean;
  adultDeclaration: boolean;
}

type Field = keyof FormValues;
type FieldCodes = Partial<Record<Field, string>>;

// Ordem visual dos campos: define a ordem do resumo de erros.
const FIELD_ORDER: readonly Field[] = [
  "email",
  "password",
  "displayName",
  "acceptTerms",
  "adultDeclaration",
];

const FIELD_IDS: Readonly<Record<Field, string>> = {
  email: "cadastro-email",
  password: "cadastro-senha",
  displayName: "cadastro-nome",
  acceptTerms: "cadastro-termos",
  adultDeclaration: "cadastro-maioridade",
};

const DEFAULT_TERMS_URL = "/termos";
const DEFAULT_PRIVACY_URL = "/privacidade";

// Abre o link em nova aba sem que o clique chegue ao rotulo do checkbox (A11Y-24).
function openInNewTab(event: Event) {
  const anchor = (event.target as HTMLElement).closest("a");
  if (!anchor) return;
  event.preventDefault();
  window.open(anchor.href, "_blank", "noopener,noreferrer");
}

function readValues(form: HTMLFormElement): FormValues {
  const data = new FormData(form);
  const text = (name: string) => {
    const value = data.get(name);
    return typeof value === "string" ? value : "";
  };
  return {
    email: text("email"),
    password: text("password"),
    displayName: text("displayName"),
    acceptTerms: data.has("acceptTerms"),
    adultDeclaration: data.has("adultDeclaration"),
  };
}

function validate(values: FormValues): FieldCodes {
  const codes: FieldCodes = {};
  const email = validateEmail(values.email);
  const password = validatePassword(values.password);
  const displayName = validateDisplayName(values.displayName);
  if (email) codes.email = email;
  if (password) codes.password = password;
  if (displayName) codes.displayName = displayName;
  if (!values.acceptTerms) codes.acceptTerms = "required";
  if (!values.adultDeclaration) codes.adultDeclaration = "required";
  return codes;
}

// Traduz o problema do servidor em um codigo por campo; texto do servidor nunca e usado.
function serverFieldCodes(error: ApiError): FieldCodes {
  const codes: FieldCodes = {};
  for (const field of FIELD_ORDER) {
    const first = error.fieldErrors[field]?.[0];
    if (first) codes[field] = first;
  }
  if (error.code === "email_unavailable") codes.email = "unavailable";
  return codes;
}

export function CadastroPage(): ReactNode {
  const client = useApiClient();
  const refreshSession = useRefreshSession();
  const navigate = useNavigate();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const termsRef = useRef<HTMLDivElement>(null);
  const valuesRef = useRef<FormValues | null>(null);
  const [terms, setTerms] = useState<TermsInfo | null>(null);
  const [codes, setCodes] = useState<FieldCodes>({});
  const [summaryKey, setSummaryKey] = useState(0);
  const [passwordKey, setPasswordKey] = useState(0);
  const [termsKey, setTermsKey] = useState(0);

  useHeadingFocusOnTransition(headingRef);

  useEffect(() => {
    const container = termsRef.current;
    container?.addEventListener("click", openInNewTab);
    return () => container?.removeEventListener("click", openInNewTab);
  }, []);

  useEffect(() => {
    let active = true;
    client
      .get<TermsInfo>(TERMS_PATH)
      .then((info) => {
        if (active) setTerms(info);
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [client]);

  const showErrors = useCallback((next: FieldCodes, clearPassword: boolean): void => {
    setCodes(next);
    setSummaryKey((key) => key + 1);
    if (clearPassword) setPasswordKey((key) => key + 1);
  }, []);

  // Depois de version_mismatch: le a versao vigente e pede novo aceite (REQ-043).
  const rereadTerms = useCallback(async (): Promise<void> => {
    try {
      setTerms(await client.get<TermsInfo>(TERMS_PATH));
    } catch {
      setTerms(null); // o proximo envio le a versao de novo
    }
    setTermsKey((key) => key + 1);
  }, [client]);

  const register = useCallback(
    async (signal: AbortSignal): Promise<void> => {
      const values = valuesRef.current;
      if (!values) return;
      try {
        const termsVersion = terms?.version ?? (await client.get<TermsInfo>(TERMS_PATH)).version;
        await client.post(REGISTER_PATH, {
          email: values.email,
          password: values.password,
          displayName: values.displayName,
          acceptTerms: values.acceptTerms,
          termsVersion,
          adultDeclaration: values.adultDeclaration,
        });
      } catch (error) {
        if (signal.aborted) return;
        const fieldCodes = error instanceof ApiError ? serverFieldCodes(error) : {};
        // Sem erro de campo (5xx, rede, outros codigos): falha geral, tratada por useSubmit.
        if (Object.keys(fieldCodes).length === 0) throw error;
        showErrors(fieldCodes, true);
        if (fieldCodes.acceptTerms === "version_mismatch") await rereadTerms();
        return;
      }
      // Resposta tardia depois do estouro de 20 s: a falha geral ja foi mostrada.
      if (signal.aborted) return;
      await refreshSession();
      navigate("/perfil", { replace: true, state: { message: SUCCESS_MESSAGE } });
    },
    [client, navigate, refreshSession, rereadTerms, showErrors, terms],
  );

  const { status, submit } = useSubmit(register);
  const sending = status === "sending";
  const failed = status === "error";

  // A senha e limpa em toda falha geral, inclusive no estouro de tempo.
  const [seenStatus, setSeenStatus] = useState(status);
  if (seenStatus !== status) {
    setSeenStatus(status);
    if (status === "error") setPasswordKey((key) => key + 1);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const values = readValues(event.currentTarget);
    const found = validate(values);
    if (Object.keys(found).length > 0) {
      showErrors(found, false);
      return;
    }
    setCodes({});
    valuesRef.current = values;
    void submit();
  }

  const messageOf = (field: Field): string | undefined => {
    const code = codes[field];
    return code ? fieldMessage(code, field) : undefined;
  };

  const summary = FIELD_ORDER.filter((field) => codes[field]).map((field) => ({
    id: FIELD_IDS[field],
    message: messageOf(field) ?? "",
  }));

  return (
    <main id="conteudo" tabIndex={-1} className="cadastro-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Criar conta
      </h1>
      {failed ? <FocusedAlert variant="error">{GENERIC_ERROR}</FocusedAlert> : null}
      <ErrorSummary key={summaryKey} errors={summary} />
      <form className="page-form" noValidate onSubmit={handleSubmit}>
        <TextField
          label="Email"
          name="email"
          id={FIELD_IDS.email}
          type="email"
          autoComplete="email"
          readOnly={sending}
          error={messageOf("email")}
        />
        <PasswordField
          key={passwordKey}
          label="Senha"
          name="password"
          id={FIELD_IDS.password}
          autoComplete="new-password"
          hint="Dica: de 8 a 128 caracteres."
          readOnly={sending}
          error={messageOf("password")}
        />
        <TextField
          label="Nome de exibição"
          name="displayName"
          id={FIELD_IDS.displayName}
          autoComplete="nickname"
          hint="Dica: de 3 a 30 caracteres. Outros Jogadores podem ter o mesmo nome."
          readOnly={sending}
          error={messageOf("displayName")}
        />
        <div ref={termsRef}>
          <Checkbox
            key={termsKey}
            name="acceptTerms"
            disabled={sending}
            id={FIELD_IDS.acceptTerms}
            label={
              <>
                Li e aceito os{" "}
                <Link href={terms?.termsUrl ?? DEFAULT_TERMS_URL} external>
                  Termos de Uso
                </Link>{" "}
                e a{" "}
                <Link href={terms?.privacyUrl ?? DEFAULT_PRIVACY_URL} external>
                  Política de Privacidade
                </Link>
                .
              </>
            }
          />
        </div>
        {messageOf("acceptTerms") ? <FormError>{messageOf("acceptTerms")}</FormError> : null}
        <Checkbox
          name="adultDeclaration"
          id={FIELD_IDS.adultDeclaration}
          disabled={sending}
          label="Declaro ter 18 anos ou mais."
        />
        {messageOf("adultDeclaration") ? (
          <FormError>{messageOf("adultDeclaration")}</FormError>
        ) : null}
        <Button type="submit" loading={sending} stableWidth>
          Criar conta
        </Button>
        <RateLimitNotice sending={sending} />
      </form>
      <p aria-hidden="true">ou</p>
      <GoogleButton onClick={() => startGoogleSignin()} />
      <p>
        Já tem conta? <Link href="/entrar">Entrar</Link>
      </p>
    </main>
  );
}
