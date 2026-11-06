import { screen } from "@testing-library/react";
import type { UserEvent } from "@testing-library/user-event";

/**
 * Preenche um campo colando o valor (QA-04): `user.type` custa ~30 ms por tecla em jsdom e estoura o testTimeout
 * sob carga. O evento `input` e os handlers do formulario sao os mesmos. Aceita o elemento ou o rotulo do campo.
 * O campo e limpo antes, entao o valor final e sempre `text`. Para testar a tecla em si, use `user.type`
 * com a anotacao `// tecla: <motivo>`.
 */
export async function fillField(
  user: UserEvent,
  field: HTMLElement | string,
  text: string,
): Promise<void> {
  const element = typeof field === "string" ? screen.getByLabelText(field) : field;
  await user.click(element);
  await user.clear(element);
  await user.paste(text);
}
