import { startTransition, useCallback, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { useApiClient, useClearSession } from "@/app/SessionProvider";
import { Button } from "@/design-system/Button";
import { Dialog } from "@/design-system/Dialog";
import { RateLimitNotice } from "@/design-system/RateLimitNotice";
import { ApiError } from "@/shared/api/apiClient";
import { useSubmit } from "@/shared/hooks/useSubmit";

import "./DeleteAccountDialog.css";

export interface ConfirmAccountDeletionDialogProps {
  open: boolean;
  onClose: () => void;
  /** 403 reauth_required / reauth_expired: o chamador volta ao primeiro dialog com a mensagem do codigo. */
  onReauthNeeded: (code: string) => void;
}

const DELETION_PATH = "/api/v1/me/deletion";
const AFTER_DELETION = "/entrar?aviso=conta-excluida";
const REAUTH_CODES = ["reauth_required", "reauth_expired"];
const SERVER_ERROR = "Não foi possível excluir sua conta agora. Tente de novo.";

function ConfirmBody({
  onClose,
  onReauthNeeded,
}: Omit<ConfirmAccountDeletionDialogProps, "open">): ReactNode {
  const client = useApiClient();
  const clearSession = useClearSession();
  const navigate = useNavigate();
  const [failed, setFailed] = useState(false);

  const remove = useCallback(async (): Promise<void> => {
    try {
      await client.post(DELETION_PATH, { confirm: true });
    } catch (error) {
      if (error instanceof ApiError && REAUTH_CODES.includes(error.code)) {
        onReauthNeeded(error.code);
      } else {
        setFailed(true);
      }
      return;
    }
    // Sessao guest e navegacao juntas: o guard nao redireciona antes da troca de rota.
    startTransition(() => {
      clearSession();
      navigate(AFTER_DELETION, { replace: true });
    });
  }, [client, navigate, onReauthNeeded, clearSession]);

  const { status, submit } = useSubmit(remove);
  const sending = status === "sending";

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending) return;
    setFailed(false);
    void submit();
  }

  return (
    <Dialog
      open
      onClose={onClose}
      role="alertdialog"
      title="Confirmar exclusão"
      description="Você confirmou sua identidade. Excluir sua conta agora?"
      busy={sending}
    >
      <form className="delete-dialog" noValidate onSubmit={handleSubmit}>
        {failed ? (
          <p className="delete-dialog__message" role="alert">
            {SERVER_ERROR}
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

export function ConfirmAccountDeletionDialog({
  open,
  ...rest
}: ConfirmAccountDeletionDialogProps): ReactNode {
  return open ? <ConfirmBody {...rest} /> : null;
}
