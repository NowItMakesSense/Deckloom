import { useCallback, useEffect, useRef, useState } from "react";
import { useHeadingFocusOnTransition } from "@/shared/hooks/useHeadingFocusOnTransition";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient, useRefreshSession } from "@/app/SessionProvider";
import { Button } from "@/design-system/Button";
import { Checkbox } from "@/design-system/Checkbox";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { FormError } from "@/design-system/FormError";
import { Link } from "@/design-system/Link";
import { Spinner } from "@/design-system/Spinner";
import { TextField } from "@/design-system/TextField";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { fieldMessage, messageFor } from "@/shared/messages/messages";
import { validateDisplayName } from "@/shared/validation/validators";
import "./pageShell.css";

const TERMS_PATH = "/api/v1/legal/terms";
const PENDING_PATH = "/api/v1/auth/google/pending";
const COMPLETE_PATH = "/api/v1/auth/google/complete-signup";
const GOOGLE_FAILED = "/entrar?aviso=google-falhou";
const SUCCESS_MESSAGE = "Conta criada. Bem-vindo(a) ao Deckloom!";
const DEFAULT_TERMS_URL = "/termos";
const DEFAULT_PRIVACY_URL = "/privacidade";

interface TermsInfo {
  version: string;
  termsUrl: string;
  privacyUrl: string;
}

// So email e nome sugerido sao usados; subject e token nunca saem da resposta (REQ-006).
interface PendingInfo {
  email: string;
  suggestedDisplayName: string;
}

interface FormValues {
  displayName: string;
  acceptTerms: boolean;
  adultDeclaration: boolean;
}

type Field = keyof FormValues;
type FieldCodes = Partial<Record<Field, string>>;

const FIELD_ORDER: readonly Field[] = ["displayName", "acceptTerms", "adultDeclaration"];

const FIELD_IDS: Readonly<Record<Field, string>> = {
  displayName: "google-nome",
  acceptTerms: "google-termos",
  adultDeclaration: "google-maioridade",
};

// Abre o link em nova aba sem que o clique chegue ao rotulo do checkbox (A11Y-24).
function openInNewTab(event: Event) {
  const anchor = (event.target as HTMLElement).closest("a");
  if (!anchor) return;
  event.preventDefault();
  window.open(anchor.href, "_blank", "noopener,noreferrer");
}

function readValues(form: HTMLFormElement): FormValues {
  const data = new FormData(form);
  const name = data.get("displayName");
  return {
    displayName: typeof name === "string" ? name : "",
    acceptTerms: data.has("acceptTerms"),
    adultDeclaration: data.has("adultDeclaration"),
  };
}

function validate(values: FormValues): FieldCodes {
  const codes: FieldCodes = {};
  const displayName = validateDisplayName(values.displayName);
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
  return codes;
}

export function GoogleSignupPage(): ReactNode {
  const client = useApiClient();
  const refreshSession = useRefreshSession();
  const navigate = useNavigate();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const termsRef = useRef<HTMLDivElement>(null);
  const valuesRef = useRef<FormValues | null>(null);
  const [pending, setPending] = useState<PendingInfo | null>(null);
  const [terms, setTerms] = useState<TermsInfo | null>(null);
  const [codes, setCodes] = useState<FieldCodes>({});
  const [notCompleted, setNotCompleted] = useState(false);
  const [summaryKey, setSummaryKey] = useState(0);

  useHeadingFocusOnTransition(headingRef);

  useEffect(() => {
    let active = true;
    client
      .get<PendingInfo>(PENDING_PATH)
      .then((info) => {
        if (active) {
          setPending({ email: info.email, suggestedDisplayName: info.suggestedDisplayName });
        }
      })
      .catch((error: unknown) => {
        // Demais falhas (rede, 5xx) ficam para a T-108.
        if (active && error instanceof ApiError && error.status === 404) {
          navigate(GOOGLE_FAILED, { replace: true });
        }
      });
    client
      .get<TermsInfo>(TERMS_PATH)
      .then((info) => {
        if (active) setTerms(info);
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [client, navigate]);

  useEffect(() => {
    const container = termsRef.current;
    container?.addEventListener("click", openInNewTab);
    return () => container?.removeEventListener("click", openInNewTab);
  }, [pending]);

  const showErrors = useCallback((next: FieldCodes): void => {
    setCodes(next);
    setSummaryKey((key) => key + 1);
  }, []);

  const completeSignup = useCallback(async (): Promise<void> => {
    const values = valuesRef.current;
    if (!values) return;
    try {
      const termsVersion = terms?.version ?? (await client.get<TermsInfo>(TERMS_PATH)).version;
      await client.post(COMPLETE_PATH, {
        displayName: values.displayName,
        acceptTerms: values.acceptTerms,
        termsVersion,
        adultDeclaration: values.adultDeclaration,
      });
    } catch (error) {
      if (!(error instanceof ApiError)) return;
      if (error.status === 404) {
        navigate(GOOGLE_FAILED, { replace: true });
      } else if (error.code === "google_not_completed") {
        setNotCompleted(true);
      } else {
        showErrors(serverFieldCodes(error));
      }
      return;
    }
    await refreshSession();
    navigate("/perfil", { replace: true, state: { message: SUCCESS_MESSAGE } });
  }, [client, navigate, refreshSession, showErrors, terms]);

  const { status, submit } = useSubmit(completeSignup);
  const sending = status === "sending";

  const cancel = useCallback(async (): Promise<void> => {
    try {
      await client.delete(PENDING_PATH);
    } catch {
      // A pendencia expira sozinha; a saida para a S2 nao depende da limpeza.
    }
    navigate("/entrar", { replace: true });
  }, [client, navigate]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const values = readValues(event.currentTarget);
    const found = validate(values);
    if (Object.keys(found).length > 0) {
      showErrors(found);
      return;
    }
    setCodes({});
    setNotCompleted(false);
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
    <main id="conteudo" tabIndex={-1} className="google-signup-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Quase lá
      </h1>
      {pending === null ? (
        <div role="status">
          <Spinner label="Aguarde um instante..." />
        </div>
      ) : (
        <>
          <p className="page-readonly-email">Email do Google: {pending.email}</p>
          <ErrorSummary key={summaryKey} errors={summary} />
          {notCompleted ? <FormError>{messageFor("google_not_completed")}</FormError> : null}
          <form className="page-form" noValidate onSubmit={handleSubmit}>
            <TextField
              label="Nome de exibição"
              name="displayName"
              id={FIELD_IDS.displayName}
              autoComplete="nickname"
              defaultValue={pending.suggestedDisplayName}
              hint="Dica: de 3 a 30 caracteres. Outros Jogadores podem ter o mesmo nome."
              error={messageOf("displayName")}
            />
            <div ref={termsRef}>
              <Checkbox
                name="acceptTerms"
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
              label="Declaro ter 18 anos ou mais."
            />
            {messageOf("adultDeclaration") ? (
              <FormError>{messageOf("adultDeclaration")}</FormError>
            ) : null}
            <Button type="submit" loading={sending}>
              Criar conta
            </Button>
          </form>
          <Button variant="secondary" onClick={() => void cancel()}>
            Cancelar
          </Button>
          {notCompleted ? (
            <p>
              <Link href="/entrar">Voltar para entrar</Link>
            </p>
          ) : null}
        </>
      )}
    </main>
  );
}
