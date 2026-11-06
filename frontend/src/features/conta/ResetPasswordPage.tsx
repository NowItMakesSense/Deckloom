import { useCallback, useEffect, useRef, useState } from "react";
import { useHeadingFocusOnTransition } from "@/shared/hooks/useHeadingFocusOnTransition";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient } from "@/app/SessionProvider";
import { Alert } from "@/design-system/Alert";
import { Button } from "@/design-system/Button";
import { ErrorSummary } from "@/design-system/ErrorSummary";
import { Link } from "@/design-system/Link";
import { APP_NAME } from "@/app/routeTitles";
import { PasswordField } from "@/design-system/PasswordField";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { fieldMessage, messageFor } from "@/shared/messages/messages";
import { validatePassword } from "@/shared/validation/validators";
import "./pageShell.css";

const CONFIRM_PATH = "/api/v1/auth/password-reset/confirm";
const PASSWORD_ID = "redefinir-senha-nova-senha";
const SUCCESS_PATH = "/entrar?aviso=senha-alterada";

// O token vem so do fragmento (#token=...), nunca da query (REQ-029, UX-11).
function readTokenFromHash(): string {
  const params = new URLSearchParams(window.location.hash.replace(/^#/, ""));
  return params.get("token") ?? "";
}

function passwordError(password: string): string | undefined {
  const code = validatePassword(password);
  if (code === null) return undefined;
  return code === "required"
    ? fieldMessage("required", "password")
    : fieldMessage(code, "newPassword");
}

const INVALID_LINK_HEADING = "Link inválido ou expirado";

function InvalidLink(): ReactNode {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useHeadingFocusOnTransition(headingRef);
  // O titulo e por estado da tela (A11Y-20): a rota e a mesma, entao o RouteTitle nao o refaz ao entrar aqui.
  useEffect(() => {
    document.title = `${INVALID_LINK_HEADING} - ${APP_NAME}`;
  }, []);
  return (
    <main id="conteudo" tabIndex={-1} className="reset-password-page">
      <h1 ref={headingRef} tabIndex={-1}>
        {INVALID_LINK_HEADING}
      </h1>
      <p>{messageFor("reset_link_invalid")}</p>
      <p>
        <Link href="/esqueci-senha" standalone>
          Solicitar novo link
        </Link>
      </p>
    </main>
  );
}

export function ResetPasswordPage(): ReactNode {
  const client = useApiClient();
  const navigate = useNavigate();
  const [token] = useState(readTokenFromHash);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const passwordRef = useRef("");
  const [linkInvalid, setLinkInvalid] = useState(false);
  const [error, setError] = useState<string | undefined>();
  const [summaryKey, setSummaryKey] = useState(0);
  const [passwordKey, setPasswordKey] = useState(0);

  // Remove o token da URL (historico e barra de endereco) assim que foi lido.
  useEffect(() => {
    if (window.location.hash !== "") {
      window.history.replaceState(null, "", window.location.pathname + window.location.search);
    }
  }, []);

  // Roda depois do foco do ErrorSummary: o campo invalido fica com o foco.
  useEffect(() => {
    if (summaryKey > 0 && error) document.getElementById(PASSWORD_ID)?.focus();
  }, [summaryKey, error, passwordKey]);

  const hasToken = token !== "";
  useHeadingFocusOnTransition(headingRef, hasToken);

  const confirm = useCallback(async (): Promise<void> => {
    try {
      await client.post(CONFIRM_PATH, { token, newPassword: passwordRef.current });
    } catch (caught) {
      if (caught instanceof ApiError && caught.code === "reset_link_invalid") {
        setLinkInvalid(true);
        return;
      }
      const codes = caught instanceof ApiError ? caught.fieldErrors["newPassword"] : undefined;
      if (caught instanceof ApiError && caught.code === "validation_failed" && codes?.length) {
        setError(fieldMessage(codes[0], "newPassword"));
        setPasswordKey((key) => key + 1);
        setSummaryKey((key) => key + 1);
        return;
      }
      throw caught;
    }
    navigate(SUCCESS_PATH, { replace: true });
  }, [client, navigate, token]);

  const { status, error: submitError, submit } = useSubmit(confirm);
  const sending = status === "sending";

  if (!hasToken || linkInvalid) return <InvalidLink />;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const value = new FormData(event.currentTarget).get("newPassword");
    const password = typeof value === "string" ? value : "";
    const found = passwordError(password);
    if (found) {
      setError(found);
      setPasswordKey((key) => key + 1);
      setSummaryKey((key) => key + 1);
      return;
    }
    setError(undefined);
    setSummaryKey(0);
    passwordRef.current = password;
    void submit();
  }

  const failure = submitError instanceof ApiError ? messageFor(submitError.code) : undefined;

  return (
    <main id="conteudo" tabIndex={-1} className="reset-password-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Criar nova senha
      </h1>
      {status === "error" ? (
        <Alert variant="error">{failure ?? messageFor("unknown_error")}</Alert>
      ) : null}
      <ErrorSummary key={summaryKey} errors={error ? [{ id: PASSWORD_ID, message: error }] : []} />
      <form className="page-form" noValidate onSubmit={handleSubmit}>
        <PasswordField
          key={passwordKey}
          label="Nova senha"
          name="newPassword"
          id={PASSWORD_ID}
          autoComplete="new-password"
          hint="Dica: de 8 a 128 caracteres."
          error={error}
        />
        <Button type="submit" loading={sending}>
          Salvar nova senha
        </Button>
      </form>
    </main>
  );
}
