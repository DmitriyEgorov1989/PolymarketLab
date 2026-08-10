export function calculateUnpersisted(
  messagesReceived: number,
  messagesPersisted: number,
): number {
  return messagesReceived - messagesPersisted;
}
