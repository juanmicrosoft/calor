export const staticMetricLabels: Record<string, { name: string; description: string }> = {
  Comprehension: { name: 'Comprehension', description: 'Static structural and semantic signals; not observed reader comprehension.' },
  Correctness: { name: 'Correctness', description: 'Static correctness signals; not a measured production defect rate.' },
  EditPrecision: { name: 'Edit Precision', description: 'Heuristic targetability and change isolation; not observed agent editing success.' },
  ErrorDetection: { name: 'Error Detection', description: 'Static detection signals; not observed bug-finding performance.' },
  GenerationAccuracy: { name: 'Generation Accuracy', description: 'Compilation and structural signals; not generation from live agent prompts.' },
  InformationDensity: { name: 'Information Density', description: 'Counted semantic elements per token under the calculator rules.' },
  RefactoringStability: { name: 'Refactoring Stability', description: 'Reference preservation under modeled transformations; not a live refactoring trial.' },
  TokenEconomics: { name: 'Token Economics', description: 'Composite of token, character, and line ratios; not a raw-token saving.' },
};

export const staticMetricOrder = Object.keys(staticMetricLabels);
