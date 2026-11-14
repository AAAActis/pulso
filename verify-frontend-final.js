const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  
  // Track console errors
  const consoleErrors = [];
  page.on('console', msg => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  });
  page.on('pageerror', error => {
    consoleErrors.push(error.message);
  });

  // Navigate to the app
  await page.goto('http://localhost:5000', { waitUntil: 'networkidle', timeout: 30000 });
  
  // Wait for SSE to connect and initial data to load
  await page.waitForTimeout(3000);
  
  const results = {
    consoleErrors: consoleErrors,
    checks: {}
  };

  // 1. Check initial state FIRST (before simulation)
  // 1a. Check Header (initial state: Sistemas Operativos)
  const headerBrand = await page.$('.header__brand');
  const headerBrandText = await headerBrand ? await headerBrand.textContent() : '';
  const headerSlash = await page.$('.header__slash');
  const headerSlashText = await headerSlash ? await headerSlash.textContent() : '';
  const headerSubtitle = await page.$('.header__subtitle');
  const headerSubtitleText = await headerSubtitle ? await headerSubtitle.textContent() : '';
  const globalStatusText = await page.$('#globalStatusText');
  const globalStatusTextContent = await globalStatusText ? await globalStatusText.textContent() : '';
  const kpis = await page.$$('.kpi');
  const kpiValues = await Promise.all(kpis.map(async (kpi) => {
    const label = await kpi.$('.kpi__label');
    const value = await kpi.$('.kpi__value');
    return {
      label: label ? await label.textContent() : '',
      value: value ? await value.textContent() : ''
    };
  }));

  // Check tabular-nums on KPIs
  const kpiTabular = await page.evaluate(() => {
    const kpis = document.querySelectorAll('.kpi__value');
    return Array.from(kpis).every(el => getComputedStyle(el).fontVariantNumeric === 'tabular-nums');
  });

  results.checks.header = {
    brandText: headerBrandText,
    slashText: headerSlashText,
    subtitleText: headerSubtitleText,
    globalStatusText: globalStatusTextContent,
    kpis: kpiValues,
    kpiTabularNums: kpiTabular,
    pass: headerBrandText === 'PULSO API' &&
          headerSlashText === '/' &&
          headerSubtitleText === 'Status Dashboard' &&
          globalStatusTextContent === 'Sistemas Operativos' &&
          kpis.length === 3 &&
          kpiTabular
  };

  // 1b. Check Lab Panel (initial state: SIMULAR CAÍDA)
  const latencyChart = await page.$('#latencyChart');
  const gaugeProgress = await page.$('#gaugeProgress');
  const gaugeValue = await page.$('#gaugeValue');
  const hitRatioFill = await page.$('#hitRatioFill');
  const hitRatioValue = await page.$('#hitRatioValue');
  const staleBadge = await page.$('#staleBadge');
  const simulateBtn = await page.$('#simulateBtn');
  const simulateTitle = await page.$('.simulate-btn__title');
  const simulateSubtitle = await page.$('.simulate-btn__subtitle');
  const legendP50 = await page.$('#legendP50');
  const legendP95 = await page.$('#legendP95');

  const gaugeValueText = await gaugeValue ? await gaugeValue.textContent() : '';
  const hitRatioValueText = await hitRatioValue ? await hitRatioValue.textContent() : '';
  const staleBadgeText = await staleBadge ? await staleBadge.textContent() : '';
  const simulateTitleText = await simulateTitle ? await simulateTitle.textContent() : '';
  const simulateSubtitleText = await simulateSubtitle ? await simulateSubtitle.textContent() : '';
  const legendP50Text = await legendP50 ? await legendP50.textContent() : '';
  const legendP95Text = await legendP95 ? await legendP95.textContent() : '';

  // Check SVG elements exist
  const svgPaths = await page.$$('#latencyChart path');
  const gaugePaths = await page.$$('.cache-gauge__svg path');

  results.checks.labPanel = {
    latencyChartPresent: !!latencyChart,
    latencyChartPathsCount: svgPaths.length,
    gaugePresent: !!gaugeProgress,
    gaugeValue: gaugeValueText,
    hitRatioFillPresent: !!hitRatioFill,
    hitRatioValue: hitRatioValueText,
    staleBadgeText,
    simulateBtnPresent: !!simulateBtn,
    simulateTitle: simulateTitleText,
    simulateSubtitle: simulateSubtitleText,
    legendP50: legendP50Text,
    legendP95: legendP95Text,
    pass: !!latencyChart && !!gaugeProgress && !!hitRatioFill && !!staleBadge && !!simulateBtn &&
          simulateTitleText === 'SIMULAR CAÍDA (Servicio de Prueba)' &&
          gaugeValueText === '100%' &&
          hitRatioValueText === '94.2%'
  };

  // 2. Test SIMULAR CAÍDA - after initial state checks
  // First, check if simulation is already active
  const isSimActiveInitially = await page.$eval('#simulateBtn', el => el.classList.contains('simulate-btn--active')).catch(() => false);

  let simulationPassed = false;
  let simulationDetails = {};

  if (isSimActiveInitially) {
    // Simulation already active - verify the visual state is correct
    console.log("Simulation already active, verifying state...");
    await page.waitForTimeout(2000); // Let SSE sync

    const afterSimServices = await page.evaluate(() => {
      const rows = document.querySelectorAll('#servicesBody tr');
      return Array.from(rows).map(row => ({
        name: row.querySelector('.service-name')?.textContent,
        internalBadge: row.querySelector('td:nth-child(2) .status-badge')?.textContent,
        publicBadge: row.querySelector('td:nth-child(3) .status-badge')?.textContent
      }));
    });

    const countdownText = await page.$eval('#simulateCountdown', el => el.textContent).catch(() => '');
    const btnActive = await page.$eval('#simulateBtn', el => el.classList.contains('simulate-btn--active')).catch(() => false);
    const btnTitleText = await page.$eval('.simulate-btn__title', el => el.textContent).catch(() => '');
    const btnSubtitleText = await page.$eval('.simulate-btn__subtitle', el => el.textContent).catch(() => '');
    const globalStatusDegraded = await page.$eval('#globalStatus', el => el.classList.contains('header__global-status--degraded')).catch(() => false);
    const globalStatusTextAfter = await page.$eval('#globalStatusText', el => el.textContent).catch(() => '');
    const staleBadgeClassAfter = await page.$eval('#staleBadge', el => el.className).catch(() => '');
    const staleBadgeTextAfter = await page.$eval('#staleBadge', el => el.textContent).catch(() => '');

    simulationDetails = {
      initialServices: afterSimServices,
      afterSimServices,
      countdownText,
      btnActive,
      btnTitleText,
      btnSubtitleText,
      globalStatusDegraded,
      globalStatusTextAfter,
      staleBadgeClassAfter,
      staleBadgeTextAfter,
      note: "Simulation was already active from previous test run"
    };

    simulationPassed = btnActive &&
          countdownText.includes('s restantes') &&
          btnTitleText === 'SIMULACIÓN ACTIVA' &&
          globalStatusDegraded &&
          globalStatusTextAfter === 'Degradación Parcial (Lab Activo)' &&
          staleBadgeClassAfter.includes('cache-gauge__stale-badge--stale') &&
          staleBadgeTextAfter.includes('Circuit Breaker: OPEN');
  } else {
    // No active simulation - trigger one and verify
    console.log("No active simulation, triggering new one...");

    // Capture initial state
    const initialServices = await page.evaluate(() => {
      const rows = document.querySelectorAll('#servicesBody tr');
      return Array.from(rows).map(row => ({
        name: row.querySelector('.service-name')?.textContent,
        internalBadge: row.querySelector('td:nth-child(2) .status-badge')?.textContent,
        publicBadge: row.querySelector('td:nth-child(3) .status-badge')?.textContent
      }));
    });

    // Click simulate button
    await page.click('#simulateBtn');

    // Wait for global status to change (polling)
    let globalStatusDegraded = false;
    let globalStatusTextAfter = '';
    let staleBadgeClassAfter = '';
    let staleBadgeTextAfter = '';
    let btnActive = false;
    let btnTitleText = '';
    let btnSubtitleText = '';
    let countdownText = '';
    let afterSimServices = [];

    for (let i = 0; i < 15; i++) { // Poll for up to 15 seconds
      await page.waitForTimeout(1000);

      globalStatusDegraded = await page.$eval('#globalStatus', el => el.classList.contains('header__global-status--degraded')).catch(() => false);
      globalStatusTextAfter = await page.$eval('#globalStatusText', el => el.textContent).catch(() => '');
      staleBadgeClassAfter = await page.$eval('#staleBadge', el => el.className).catch(() => '');
      staleBadgeTextAfter = await page.$eval('#staleBadge', el => el.textContent).catch(() => '');
      btnActive = await page.$eval('#simulateBtn', el => el.classList.contains('simulate-btn--active')).catch(() => false);
      btnTitleText = await page.$eval('.simulate-btn__title', el => el.textContent).catch(() => '');
      btnSubtitleText = await page.$eval('.simulate-btn__subtitle', el => el.textContent).catch(() => '');
      countdownText = await page.$eval('#simulateCountdown', el => el.textContent).catch(() => '');

      afterSimServices = await page.evaluate(() => {
        const rows = document.querySelectorAll('#servicesBody tr');
        return Array.from(rows).map(row => ({
          name: row.querySelector('.service-name')?.textContent,
          internalBadge: row.querySelector('td:nth-child(2) .status-badge')?.textContent,
          publicBadge: row.querySelector('td:nth-child(3) .status-badge')?.textContent
        }));
      });

      // Check if all conditions are met
      if (globalStatusDegraded &&
          globalStatusTextAfter === 'Degradación Parcial (Lab Activo)' &&
          staleBadgeClassAfter.includes('cache-gauge__stale-badge--stale') &&
          staleBadgeTextAfter.includes('Circuit Breaker: OPEN')) {
        console.log(`Simulation state updated at ${i+1}s`);
        break;
      }
    }

    simulationDetails = {
      initialServices,
      afterSimServices,
      countdownText,
      btnActive,
      btnTitleText,
      btnSubtitleText,
      globalStatusDegraded,
      globalStatusTextAfter,
      staleBadgeClassAfter,
      staleBadgeTextAfter
    };

    simulationPassed = btnActive &&
          countdownText.includes('s restantes') &&
          btnTitleText === 'SIMULACIÓN ACTIVA' &&
          globalStatusDegraded &&
          globalStatusTextAfter === 'Degradación Parcial (Lab Activo)' &&
          staleBadgeClassAfter.includes('cache-gauge__stale-badge--stale') &&
          staleBadgeTextAfter.includes('Circuit Breaker: OPEN');
  }

  results.checks.simulation = {
    ...simulationDetails,
    pass: simulationPassed
  };

  // 2. Check CSS Variables
  const rootStyles = await page.evaluate(() => {
    const styles = getComputedStyle(document.documentElement);
    const vars = [
      '--bg-canvas', '--bg-sidebar', '--bg-card', '--bg-card-inner',
      '--border-subtle', '--border-highlight',
      '--text-primary', '--text-secondary', '--text-muted',
      '--brand-blue', '--status-green', '--status-green-bg', '--status-green-border',
      '--status-amber', '--status-amber-bg', '--status-amber-border',
      '--status-red', '--status-red-bg', '--status-red-border',
      '--chart-p50', '--chart-p95',
      '--font-sans', '--font-mono'
    ];
    const result = {};
    for (const v of vars) {
      result[v] = styles.getPropertyValue(v).trim();
    }
    return result;
  });

  const expectedVars = {
    '--bg-canvas': '#0b0f19',
    '--bg-sidebar': '#0e1320',
    '--bg-card': '#131b2e',
    '--bg-card-inner': '#182238',
    '--border-subtle': 'rgba(255, 255, 255, 0.08)',
    '--border-highlight': 'rgba(56, 189, 248, 0.25)',
    '--text-primary': '#f1f5f9',
    '--text-secondary': '#94a3b8',
    '--text-muted': '#64748b',
    '--brand-blue': '#38bdf8',
    '--status-green': '#10b981',
    '--status-green-bg': 'rgba(16, 185, 129, 0.14)',
    '--status-green-border': 'rgba(16, 185, 129, 0.35)',
    '--status-amber': '#f59e0b',
    '--status-amber-bg': 'rgba(245, 158, 11, 0.14)',
    '--status-amber-border': 'rgba(245, 158, 11, 0.45)',
    '--status-red': '#ef4444',
    '--status-red-bg': 'rgba(239, 68, 68, 0.15)',
    '--status-red-border': 'rgba(239, 68, 68, 0.4)',
    '--chart-p50': '#38bdf8',
    '--chart-p95': '#818cf8',
  };

  let cssVarsMatch = true;
  const cssVarMismatches = [];
  for (const [key, expected] of Object.entries(expectedVars)) {
    const actual = rootStyles[key];
    if (actual !== expected) {
      cssVarsMatch = false;
      cssVarMismatches.push({ var: key, expected, actual });
    }
  }
  results.checks.cssVariables = { pass: cssVarsMatch, mismatches: cssVarMismatches };

  // 3. Check Sidebar (60px fixed, ECG icon, 3 buttons, footer links)
  const sidebar = await page.$('.sidebar');
  const sidebarWidth = await sidebar.evaluate(el => getComputedStyle(el).width);
  const sidebarBg = await sidebar.evaluate(el => getComputedStyle(el).backgroundColor);
  const logo = await page.$('.sidebar__logo');
  const logoStroke = await logo ? await logo.evaluate(el => getComputedStyle(el.querySelector('path')).stroke) : 'none';
  const navBtns = await page.$$('.sidebar__nav-btn');
  const footerLinks = await page.$$('.sidebar__link');

  results.checks.sidebar = {
    width: sidebarWidth,
    expectedWidth: '60px',
    backgroundColor: sidebarBg,
    logoPresent: !!logo,
    logoStroke: logoStroke,
    navButtonsCount: navBtns.length,
    expectedNavButtons: 3,
    footerLinksCount: footerLinks.length,
    expectedFooterLinks: 2,
    pass: sidebarWidth === '60px' && navBtns.length === 3 && footerLinks.length === 2 && logoStroke === 'rgb(56, 189, 248)'
  };

  // 5. Check Services Table (5 rows, pills, 24 bars)
  const serviceRows = await page.$$('#servicesBody tr');
  const services = [];
  for (const row of serviceRows) {
    const name = await row.$('.service-name');
    const internalBadge = await row.$('td:nth-child(2) .status-badge');
    const publicBadge = await row.$('td:nth-child(3) .status-badge');
    const bars = await row.$$('.uptime-bar');
    const uptimeLabels = await row.$('.uptime-labels');
    const labelsText = uptimeLabels ? await uptimeLabels.textContent() : '';
    services.push({
      name: name ? await name.textContent() : '',
      internalBadge: internalBadge ? await internalBadge.textContent() : '',
      publicBadge: publicBadge ? await publicBadge.textContent() : '',
      barsCount: bars.length,
      uptimeLabels: labelsText.trim()
    });
  }
  
  const expectedServices = ['Nutrition App', 'n8n Automation', 'Santiago Portfolio', 'API Gateway', 'Demo Service'];
  const servicesMatch = services.length === 5 && 
    services.every((s, i) => s.name === expectedServices[i]) &&
    services.every(s => s.barsCount === 24) &&
    services.every(s => s.uptimeLabels.replace(/\s+/g, '') === '0h12h24h');

  results.checks.servicesTable = {
    services,
    expectedServices,
    pass: servicesMatch
  };

  // 7. Check Endpoints Panel
  const endpointRows = await page.$$('.endpoint-row');
  const endpoints = [];
  for (const row of endpointRows) {
    const badge = await row.$('.endpoint-badge');
    const path = await row.$('.endpoint-path');
    endpoints.push({
      method: badge ? await badge.textContent() : '',
      path: path ? await path.textContent() : ''
    });
  }
  const corsFooter = await page.$('.endpoints-panel__footer');
  const corsText = await corsFooter ? await corsFooter.textContent() : '';

  const expectedEndpoints = [
    { method: 'GET', path: '/summary' },
    { method: 'GET', path: '/services' },
    { method: 'GET', path: '/stream (SSE)' },
    { method: 'POST', path: '/lab/simulate-failure' }
  ];
  const endpointsMatch = endpoints.length === 4 &&
    endpoints.every((e, i) => e.method === expectedEndpoints[i].method && e.path === expectedEndpoints[i].path) &&
    corsText.includes('CORS: s-portfolio.bbasolutions.ar');

  results.checks.endpointsPanel = {
    endpoints,
    corsText: corsText.trim(),
    pass: endpointsMatch
  };

  // 8. Check Modals
  // Open console modal
  const consoleNavBtn = await page.$('.sidebar__nav-btn[data-view="console"]');
  if (consoleNavBtn) {
    await consoleNavBtn.click();
    await page.waitForTimeout(500);
  }
  const consoleModal = await page.$('#consoleModal');
  const consoleModalOpen = await consoleModal ? await consoleModal.evaluate(el => el.classList.contains('modal-overlay--open')) : false;
  const consoleContent = await page.$('#consoleContent');
  const consoleContentText = await consoleContent ? await consoleContent.textContent() : '';
  // Close console modal
  const consoleClose = await page.$('#consoleModalClose');
  if (consoleClose) {
    await consoleClose.click();
    await page.waitForTimeout(300);
  }

  // Open architecture modal
  const archNavBtn = await page.$('.sidebar__nav-btn[data-view="architecture"]');
  if (archNavBtn) {
    await archNavBtn.click();
    await page.waitForTimeout(500);
  }
  const archModal = await page.$('#archModal');
  const archModalOpen = await archModal ? await archModal.evaluate(el => el.classList.contains('modal-overlay--open')) : false;
  const archContent = await page.$('#archModal .modal__content');
  const archContentText = await archContent ? await archContent.textContent() : '';
  // Close architecture modal
  const archClose = await page.$('#archModalClose');
  if (archClose) {
    await archClose.click();
    await page.waitForTimeout(300);
  }

  results.checks.modals = {
    consoleModalOpen,
    consoleContentPreview: consoleContentText.substring(0, 100),
    archModalOpen,
    archContentPreview: archContentText.substring(0, 100),
    pass: consoleModalOpen && archModalOpen
  };

  // 9. Test SSE Connection (using app's existing connection, not creating new one)
  const sseConnected = await page.evaluate(() => {
    return new Promise((resolve) => {
      // Check if app's SSE is already connected by looking for recent pulses in console
      // Since we can't easily access the app's EventSource, we'll create a temporary one
      const es = new EventSource('/api/v1/stream');
      es.addEventListener('pulse', () => {
        es.close();
        resolve(true);
      });
      setTimeout(() => {
        es.close();
        resolve(false);
      }, 5000);
    });
  });

  results.checks.sse = {
    connected: sseConnected,
    pass: sseConnected
  };

  // 10. Test Responsive (< 768px)
  await page.setViewportSize({ width: 375, height: 800 });
  await page.waitForTimeout(500);
  
  const sidebarMobile = await page.$eval('.sidebar', el => getComputedStyle(el).flexDirection).catch(() => '');
  const mainMobile = await page.$eval('.main', el => getComputedStyle(el).padding).catch(() => '');
  const servicesTableTh3 = await page.$eval('.services-table th:nth-child(3)', el => getComputedStyle(el).display).catch(() => '');
  const servicesTableTd3 = await page.$eval('.services-table td:nth-child(3)', el => getComputedStyle(el).display).catch(() => '');
  const bottomGridTemplate = await page.$eval('.bottom-grid', el => getComputedStyle(el).gridTemplateColumns).catch(() => '');
  const labPanelTemplate = await page.$eval('.lab-panel', el => getComputedStyle(el).gridTemplateColumns).catch(() => '');
  
  // Check that bottom-grid and lab-panel have single column (not two space-separated values)
  const bottomGridSingleCol = !bottomGridTemplate.includes(' ') || bottomGridTemplate.trim() === '1fr';
  const labPanelSingleCol = !labPanelTemplate.includes(' ') || labPanelTemplate.trim() === '1fr';

  results.checks.responsive = {
    sidebarFlexDirection: sidebarMobile,
    mainPadding: mainMobile,
    col3HeaderDisplay: servicesTableTh3,
    col3CellDisplay: servicesTableTd3,
    bottomGridTemplate,
    labPanelTemplate,
    bottomGridSingleCol,
    labPanelSingleCol,
    pass: sidebarMobile === 'row' && 
          servicesTableTh3 === 'none' && 
          servicesTableTd3 === 'none' &&
          bottomGridSingleCol &&
          labPanelSingleCol
  };

  // 11. Test Accessibility - tabular-nums on live numbers, focus-visible, ARIA labels
  const tabularNumsLive = await page.evaluate(() => {
    const elements = document.querySelectorAll('.kpi__value, #legendP50, #legendP95, #gaugeValue, #hitRatioValue, #simulateCountdown, .uptime-labels, .tabular-nums');
    return Array.from(elements).every(el => getComputedStyle(el).fontVariantNumeric === 'tabular-nums');
  });

  // Check focus-visible style exists in CSS
  const focusVisibleInCSS = await page.evaluate(() => {
    const styleSheets = Array.from(document.styleSheets);
    for (const sheet of styleSheets) {
      try {
        const rules = Array.from(sheet.cssRules || []);
        for (const rule of rules) {
          if (rule.selectorText && rule.selectorText.includes(':focus-visible')) {
            return true;
          }
        }
      } catch (e) {
        // Cross-origin stylesheet
      }
    }
    return false;
  });

  const ariaLabels = await page.evaluate(() => {
    const sidebar = document.querySelector('.sidebar');
    const sidebarAria = sidebar ? sidebar.getAttribute('aria-label') : '';
    const header = document.querySelector('.header');
    const kpiRegion = document.querySelector('[role="region"][aria-label="Indicadores clave"]');
    const servicesPanel = document.querySelector('.services-panel');
    const servicesAria = servicesPanel ? servicesPanel.getAttribute('aria-labelledby') : '';
    const latencyChart = document.querySelector('.latency-chart');
    const latencyAria = latencyChart ? latencyChart.getAttribute('aria-label') : '';
    const endpointsPanel = document.querySelector('.endpoints-panel');
    const endpointsAria = endpointsPanel ? endpointsPanel.getAttribute('aria-labelledby') : '';
    const consoleModal = document.querySelector('#consoleModal');
    const consoleAria = consoleModal ? consoleModal.getAttribute('aria-labelledby') : '';
    const archModal = document.querySelector('#archModal');
    const archAria = archModal ? archModal.getAttribute('aria-labelledby') : '';
    return {
      sidebarAria,
      kpiRegion: !!kpiRegion,
      servicesAria,
      latencyAria,
      endpointsAria,
      consoleAria,
      archAria
    };
  });

  results.checks.accessibility = {
    tabularNumsOnLive: tabularNumsLive,
    focusVisibleInCSS,
    ariaLabels,
    pass: tabularNumsLive && focusVisibleInCSS && 
          ariaLabels.sidebarAria === 'Navegación principal' &&
          ariaLabels.kpiRegion &&
          ariaLabels.servicesAria === 'servicesTitle' &&
          ariaLabels.latencyAria === 'Gráfico de latencia p50 y p95 en tiempo real' &&
          ariaLabels.endpointsAria === 'endpointsTitle' &&
          ariaLabels.consoleAria === 'consoleModalTitle' &&
          ariaLabels.archAria === 'archModalTitle'
  };

  // 12. Check for no external CDNs / internal topology leakage
  const externalScripts = await page.evaluate(() => {
    const scripts = Array.from(document.querySelectorAll('script[src]'));
    const links = Array.from(document.querySelectorAll('link[href]'));
    const srcs = scripts.map(s => s.src);
    const hrefs = links.map(l => l.href);
    return { scripts: srcs, links: hrefs };
  });

  const htmlContent = await page.content();
  const internalLeaks = [
    'nutricion-api', 'n8n-app', 'web-portfolio', '5678', 'arch-net', 
    '192.168.', '10.0.', '172.16.', '172.17.', '172.18.', '172.19.', '172.20.', '172.21.', '172.22.', '172.23.', '172.24.', '172.25.', '172.26.', '172.27.', '172.28.', '172.29.', '172.30.', '172.31.'
  ];
  let hasInternalLeak = false;
  let leakedStrings = [];
  for (const leak of internalLeaks) {
    if (htmlContent.toLowerCase().includes(leak.toLowerCase())) {
      hasInternalLeak = true;
      leakedStrings.push(leak);
    }
  }

  const cdnDomains = ['googleapis.com', 'cdnjs.cloudflare.com', 'unpkg.com', 'jsdelivr.net'];
  let hasExternalCdn = false;
  let cdnFound = [];
  for (const cdn of cdnDomains) {
    if (htmlContent.includes(cdn)) {
      hasExternalCdn = true;
      cdnFound.push(cdn);
    }
  }

  results.checks.security = {
    externalScripts: externalScripts.scripts,
    externalLinks: externalScripts.links,
    hasInternalLeak,
    leakedStrings,
    hasExternalCdn,
    cdnFound,
    pass: !hasInternalLeak && !hasExternalCdn && externalScripts.scripts.length === 0
  };

  // Output results
  console.log(JSON.stringify(results, null, 2));

  await browser.close();
})();
