type GTagEvent = {
  action: string;
  category: string;
  label?: string;
  value?: number;
};

function sendEvent({ action, category, label, value }: GTagEvent) {
  if (typeof window !== 'undefined' && typeof window.gtag === 'function') {
    window.gtag('event', action, {
      event_category: category,
      event_label: label,
      value,
    });
  }
}

// --- High Priority ---

export function trackCtaClick(button: 'get_started' | 'github') {
  sendEvent({ action: 'cta_click', category: 'engagement', label: button });
}

export function trackAskCalorClick(location: 'header' | 'footer' | 'mobile_menu' | 'homepage') {
  sendEvent({ action: 'ask_calor_click', category: 'conversion', label: location });
}

export function trackInstallCommandCopy(step: string) {
  sendEvent({ action: 'install_command_copy', category: 'conversion', label: step });
}

export function trackCodeCopy(language: string) {
  sendEvent({ action: 'code_copy', category: 'engagement', label: language });
}

export function trackDocsPageView(slug: string) {
  sendEvent({ action: 'docs_page_view', category: 'navigation', label: slug });
}

export function trackBenchmarkResultsView() {
  sendEvent({ action: 'benchmark_results_view', category: 'engagement' });
}

// --- Medium Priority ---

export function trackCodeComparisonTab(tab: 'calor' | 'csharp') {
  sendEvent({ action: 'code_comparison_tab', category: 'engagement', label: tab });
}

export function trackFeatureLearnMore(feature: string) {
  sendEvent({ action: 'feature_learn_more', category: 'engagement', label: feature });
}

export function trackVSCodeExtensionClick(action: 'marketplace' | 'copy_command') {
  sendEvent({ action: 'vscode_extension_click', category: 'conversion', label: action });
}

export function trackBenchmarkDetailClick() {
  sendEvent({ action: 'benchmark_detail_click', category: 'engagement' });
}

export function trackProgramTableSort(field: string) {
  sendEvent({ action: 'program_table_sort', category: 'engagement', label: field });
}

export function trackProgramTableFilter(level: string) {
  sendEvent({ action: 'program_table_filter', category: 'engagement', label: level });
}

export function trackDarkModeToggle(mode: 'dark' | 'light') {
  sendEvent({ action: 'dark_mode_toggle', category: 'preferences', label: mode });
}

// --- Low Priority ---

export function trackScrollDepth(depth: number) {
  sendEvent({ action: 'scroll_depth', category: 'engagement', value: depth, label: `${depth}%` });
}

export function trackSidebarSectionExpand(section: string) {
  sendEvent({ action: 'sidebar_section_expand', category: 'navigation', label: section });
}

export function trackTocAnchorClick(heading: string) {
  sendEvent({ action: 'toc_anchor_click', category: 'navigation', label: heading });
}

export function trackOutboundLink(url: string) {
  sendEvent({ action: 'outbound_link', category: 'navigation', label: url });
}
