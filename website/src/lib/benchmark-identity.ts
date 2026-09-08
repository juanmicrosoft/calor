// Corpus IDs are local to their source suite. Names disambiguate the published
// snapshot; fail assembly if a future dataset needs a richer source identity.
export function identifyPrograms<T extends { id: string; name: string }>(
  programs: readonly T[]
): Array<T & { identity: string }> {
  const identities = new Set<string>();
  return programs.map(program => {
    const identity = JSON.stringify([program.id, program.name]);
    if (identities.has(identity)) {
      throw new Error(`Duplicate benchmark program identity: ${identity}`);
    }
    identities.add(identity);
    return { ...program, identity };
  });
}
