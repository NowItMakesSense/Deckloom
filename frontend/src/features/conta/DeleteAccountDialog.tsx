import { startTransition, useCallback, useId, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient, useClearSession } from "@/app/SessionProvider";
import { Button } from "@/design-system/Button";
import { Checkbox } from "@/design-system/Checkbox";
import { Dialog } from "@/design-system/Dialog";
import { PasswordField } from "@/design-system/PasswordField";
import { RateLimitNotice } from "@/design-system/RateLimitNotice";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";
import { messageFor } from "@/shared/messages/messages";

import "./DeleteAccountDialog.css";

export interface DeleteAccountDialogProps {
  open: boolean;
  onClose: () => void;
}

const DELETION_PATH = "/api/v1/me/deletion";
const AFTER_DELETION = "/entrar?aviso=conta-excluida";

const DESCRIPTION =
  "Esta ação é imediata e não pode ser desfeita. Vamos excluir permanentemente seus dados pessoais (email, nome, bio, preferências, avatar) e todos os seus decks.";
const CHECK_LABEL = "Entendo que a exclusão é definitiva.";
const NOT_CHECKED = "Marque a caixa para confirmar que entende que a exclusão é definitiva.";
const NO_PASSWORD = "Informe sua senha para confirmar.";
const WRONG_PASSWORD = "Senha incorreta. Sua conta não foi excluída.";
const SERVER_ERROR = "Não foi possível excluir sua conta agora. Tente de novo.";

const FAILURE_TEXT: Readonly<Record<Failure, string>> = {
  "wrong-password": WRONG_PASSWORD,
  "rate-limited": messageFor("too_many_attempts"),
  server: SERVER_ERROR,
};

interface FieldErrors {
  box?: string;
  password?: string;
}

type Failure = "wrong-password" | "rate-limited" | "server";

// Montado so enquanto aberto: cada abertura recomeca limpa (sem cache).
function DeleteAccountBody({ onClose }: { onClose: () => void }): ReactNode {
  const client = useApiClient();
  const clearSession = useClearSession();
  const navigate = useNavigate();
  const uid = useId();
  const boxId = `${uid}-confirm`;
  const boxErrorId = `${boxId}-error`;
  const passwordId = `${uid}-password`;
  const passwordErrorId = `${passwordId}-error`;
  const passwordRef = useRef("");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [failure, setFailure] = useState<Failure | undefined>();

  const remove = useCallback(async (): Promise<void> => {
    try {
      await client.post(DELETION_PATH, { confirm: true, password: passwordRef.current });
    } catch (error) {
      if (error instanceof ApiError && error.code === "incorrect_password") {
        setFailure("wrong-password");
        document.getElementById(passwordId)?.focus();
      } else if (error instanceof ApiError && error.code === "too_many_attempts") {
        setFailure("rate-limited");
      } else {
        setFailure("server");
      }
      return;
    }
    // Sessao guest e navegacao juntas: o guard nao redireciona antes da troca de rota.
    startTransition(() => {
      clearSession();
      navigate(AFTER_DELETION, { replace: true });
    });
  }, [client, navigate, passwordId, clearSession]);

  const { status, submit } = useSubmit(remove);
  const sending = status === "sending";

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    const data = new FormData(event.currentTarget);
    const password = data.get("password");
    const found: FieldErrors = {};
    if (!data.has("confirm")) found.box = NOT_CHECKED;
    if (typeof password !== "string" || password === "") found.password = NO_PASSWORD;
    setErrors(found);
    setFailure(undefined);
    if (found.box || found.password) {
      document.getElementById(found.box ? boxId : passwordId)?.focus();
      return;
    }
    passwordRef.current = password as string;
    void submit();
  }

  const passwordDescribedBy = errors.password ? passwordErrorId : undefined;

  return (
    <Dialog
      open
      onClose={onClose}
      title="Excluir conta"
      description={DESCRIPTION}
      role="alertdialog"
      busy={sending}
    >
      <form className="delete-dialog" noValidate onSubmit={handleSubmit}>
        <div>
          <Checkbox
            name="confirm"
            id={boxId}
            label={CHECK_LABEL}
            describedBy={errors.box ? boxErrorId : undefined}
          />
          {errors.box ? (
            <p className="delete-dialog__message" id={boxErrorId}>
              {errors.box}
            </p>
          ) : null}
        </div>
        <div>
          <PasswordField
            label="Senha"
            name="password"
            id={passwordId}
            autoComplete="current-password"
            readOnly={sending}
            stableLabel
            describedBy={passwordDescribedBy}
          />
          {errors.password ? (
            <p className="delete-dialog__message" id={passwordErrorId}>
              {errors.password}
            </p>
          ) : null}
        </div>
        {failure ? (
          <p className="delete-dialog__message" role="alert">
            {FAILURE_TEXT[failure]}
          </p>
        ) : null}
        <RateLimitNotice sending={sending} />
        <div className="delete-dialog__actions">
          <Button variant="secondary">Cancelar</Button>
          <Button type="submit" variant="danger" loading={sending}>
            Excluir minha conta definitivamente
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

export function DeleteAccountDialog({ open, onClose }: DeleteAccountDialogProps): ReactNode {
  return open ? <DeleteAccountBody onClose={onClose} /> : null;
}
