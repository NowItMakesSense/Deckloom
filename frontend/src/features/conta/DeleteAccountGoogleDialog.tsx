import { useEffect, useId, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { Alert } from "@/design-system/Alert";
import { Button } from "@/design-system/Button";
import { Checkbox } from "@/design-system/Checkbox";
import { Dialog } from "@/design-system/Dialog";

import "./DeleteAccountDialog.css";

export interface DeleteAccountGoogleDialogProps {
  open: boolean;
  onClose: () => void;
  /** Aviso exibido no topo (reautenticacao exigida ou expirada); foca a caixa ao abrir. */
  notice?: string;
}

const REAUTH_PATH = "/api/v1/auth/google/start?intent=reauth";

const DESCRIPTION =
  "Esta ação é imediata e não pode ser desfeita. Vamos excluir permanentemente seus dados pessoais (email, nome, bio, preferências, avatar) e todos os seus decks.";
const REAUTH_TEXT = "Para confirmar, entre novamente com o Google.";
const CHECK_LABEL = "Entendo que a exclusão é definitiva.";
const NOT_CHECKED = "Marque a caixa para confirmar que entende que a exclusão é definitiva.";

// Montado so enquanto aberto: cada abertura recomeca limpa (sem cache).
function GoogleBody({ onClose, notice }: { onClose: () => void; notice?: string }): ReactNode {
  const uid = useId();
  const boxId = `${uid}-confirm`;
  const boxErrorId = `${boxId}-error`;
  const noticeId = `${uid}-notice`;
  const [error, setError] = useState(false);
  const [announced, setAnnounced] = useState(false);
  // Sem aviso, a regiao desmonta: o proximo aviso nasce vazio e precisa ser anunciado de novo.
  if (!notice && announced) setAnnounced(false);

  // Ordem da descricao: erro primeiro, aviso por ultimo (A11Y-31).
  const describedBy =
    [error ? boxErrorId : null, notice ? noticeId : null].filter(Boolean).join(" ") || undefined;

  // A caixa so recebe foco com o texto do aviso ja na regiao descrita (U-04, A11Y-31).
  useEffect(() => {
    if (notice && announced) document.getElementById(boxId)?.focus();
  }, [notice, announced, boxId]);

  // Navegacao completa do navegador (nao do roteador): o fluxo OAuth sai do app.
  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!new FormData(event.currentTarget).has("confirm")) {
      setError(true);
      document.getElementById(boxId)?.focus();
      return;
    }
    setError(false);
    window.location.assign(REAUTH_PATH);
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title="Excluir conta"
      description={DESCRIPTION}
      role="alertdialog"
    >
      <form className="delete-dialog" noValidate onSubmit={handleSubmit}>
        {notice ? (
          <Alert
            variant="error"
            announceAfterMount
            id={noticeId}
            onAnnounced={() => setAnnounced(true)}
          >
            {notice}
          </Alert>
        ) : null}
        <p className="delete-dialog__text">{REAUTH_TEXT}</p>
        <div>
          <Checkbox name="confirm" id={boxId} label={CHECK_LABEL} describedBy={describedBy} />
          {error ? (
            <p className="delete-dialog__message" id={boxErrorId}>
              {NOT_CHECKED}
            </p>
          ) : null}
        </div>
        <div className="delete-dialog__actions">
          <Button variant="secondary">Cancelar</Button>
          <Button type="submit" variant="danger">
            Reautenticar com o Google
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

export function DeleteAccountGoogleDialog({
  open,
  onClose,
  notice,
}: DeleteAccountGoogleDialogProps): ReactNode {
  return open ? <GoogleBody onClose={onClose} notice={notice} /> : null;
}
