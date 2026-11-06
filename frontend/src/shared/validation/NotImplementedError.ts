export class NotImplementedError extends Error {
  constructor(name: string) {
    super(`${name} not implemented`);
    this.name = "NotImplementedError";
  }
}
