// =============================================================================
// command-palette.js — Command Palette for DevCoreBlog
// =============================================================================
// A centralized command modal (like Cursor, Raycast, or Claude) that opens
// with `/` or `Ctrl+K`. Users can type terminal-style commands to navigate
// the blog quickly.
//
// Available Commands:
//   /home      → Navigate to homepage
//   /ls        → Navigate to homepage (grid view of posts)
//   /newest    → Navigate to the latest post
//   /grep X    → Search for "X" in posts
//   /whoami    → Navigate to about/portfolio page
//   /focus     → Toggle distraction-free reading mode (hides sidebar)
//   /dark      → Toggle dark mode
//   /light     → Toggle light mode
//
// Architecture:
//   - Modal HTML is injected in _Layout.cshtml
//   - This script handles keyboard events, command filtering, and execution
//   - No external dependencies — pure vanilla JavaScript
// =============================================================================

(function () {
  'use strict';

  // ── DOM References ──
  var overlay = document.getElementById('cmd-palette-overlay');
  var modal = document.getElementById('cmd-palette-modal');
  var input = document.getElementById('cmd-palette-input');
  var resultsList = document.getElementById('cmd-palette-results');
  var hintText = document.getElementById('cmd-palette-hint');

  // Guard: if modal elements don't exist, exit early
  if (!overlay || !modal || !input || !resultsList) return;

  // ── Command Registry ──
  // Each command has: name (display), cmd (what user types), desc, action (function)
  var commands = [
    {
      name: '~/home',
      cmd: '/home',
      desc: 'Ana sayfaya git',
      action: function () { window.location.href = '/'; }
    },
    {
      name: '$ ls ./posts/',
      cmd: '/ls',
      desc: 'Yazıları grid olarak görüntüle',
      action: function () { window.location.href = '/'; }
    },
    {
      name: '$ cat newest',
      cmd: '/newest',
      desc: 'En son yazıyı oku',
      action: function () { window.location.href = '/?page=1'; }
    },
    {
      name: '$ grep [keyword]',
      cmd: '/grep',
      desc: 'Yazılarda arama yap',
      action: function (args) {
        var query = args.trim();
        if (query) {
          window.location.href = '/ara?query=' + encodeURIComponent(query);
        }
      }
    },
    {
      name: '$ whoami',
      cmd: '/whoami',
      desc: 'Hakkımda sayfası',
      action: function () { window.location.href = '/'; }
    },
    {
      name: '⚡ /focus',
      cmd: '/focus',
      desc: 'Odak modu — sidebar gizle/göster',
      action: function () {
        // Toggle focus mode by hiding/showing the sidebar
        var sidebar = document.querySelector('.site-sidebar');
        var grid = document.querySelector('.site-grid');
        if (sidebar && grid) {
          sidebar.classList.toggle('sidebar-hidden');
          grid.classList.toggle('focus-mode');
        }
        closePalette();
      }
    },
    {
      name: '🌙 /dark',
      cmd: '/dark',
      desc: 'Karanlık tema',
      action: function () {
        document.documentElement.classList.add('dark');
        localStorage.setItem('theme', 'dark');
        closePalette();
      }
    },
    {
      name: '☀️ /light',
      cmd: '/light',
      desc: 'Aydınlık tema',
      action: function () {
        document.documentElement.classList.remove('dark');
        localStorage.setItem('theme', 'light');
        closePalette();
      }
    }
  ];

  // ── State ──
  var isOpen = false;
  var selectedIndex = 0;
  var filteredCommands = commands.slice(); // copy of all commands

  // ── Open / Close ──

  // Opens the command palette modal
  function openPalette() {
    isOpen = true;
    overlay.classList.add('is-open');
    modal.classList.add('is-open');
    input.value = '';
    selectedIndex = 0;
    filteredCommands = commands.slice();
    renderResults();
    // Focus the input after a small delay to ensure the modal is visible
    setTimeout(function () { input.focus(); }, 50);
  }

  // Closes the command palette modal
  function closePalette() {
    isOpen = false;
    overlay.classList.remove('is-open');
    modal.classList.remove('is-open');
    input.value = '';
  }

  // ── Filtering ──
  // Filters commands based on user input and re-renders the results list
  function filterCommands(query) {
    if (!query) {
      filteredCommands = commands.slice();
    } else {
      var lowerQuery = query.toLowerCase();
      filteredCommands = commands.filter(function (cmd) {
        return cmd.cmd.toLowerCase().indexOf(lowerQuery) !== -1 ||
               cmd.name.toLowerCase().indexOf(lowerQuery) !== -1 ||
               cmd.desc.toLowerCase().indexOf(lowerQuery) !== -1;
      });
    }
    selectedIndex = 0;
    renderResults();
  }

  // ── Rendering ──
  // Renders the filtered commands list in the modal
  function renderResults() {
    resultsList.innerHTML = '';

    if (filteredCommands.length === 0) {
      var emptyItem = document.createElement('div');
      emptyItem.className = 'cmd-result-item cmd-result-empty';
      emptyItem.textContent = 'Komut bulunamadı';
      resultsList.appendChild(emptyItem);
      return;
    }

    filteredCommands.forEach(function (cmd, index) {
      var item = document.createElement('div');
      item.className = 'cmd-result-item';
      if (index === selectedIndex) {
        item.classList.add('is-selected');
      }

      // Command name (left side)
      var nameSpan = document.createElement('span');
      nameSpan.className = 'cmd-result-name';
      nameSpan.textContent = cmd.name;

      // Command description (right side)
      var descSpan = document.createElement('span');
      descSpan.className = 'cmd-result-desc';
      descSpan.textContent = cmd.desc;

      item.appendChild(nameSpan);
      item.appendChild(descSpan);

      // Click handler for each result item
      item.addEventListener('click', function () {
        executeCommand(cmd, input.value);
      });

      // Hover handler to update selection
      item.addEventListener('mouseenter', function () {
        selectedIndex = index;
        renderResults();
      });

      resultsList.appendChild(item);
    });
  }

  // ── Execution ──
  // Executes the selected command
  function executeCommand(cmd, rawInput) {
    // For /grep command, extract the search query after "/grep "
    if (cmd.cmd === '/grep') {
      var args = rawInput.replace(/^\/grep\s*/i, '');
      cmd.action(args);
    } else {
      cmd.action();
    }
  }

  // ── Keyboard Events ──

  // Global keydown listener — opens palette with `/` or `Ctrl+K`
  document.addEventListener('keydown', function (e) {
    // Don't trigger if user is typing in an input, textarea, or contenteditable
    var tag = e.target.tagName.toLowerCase();
    var isEditable = e.target.isContentEditable;
    var isInput = tag === 'input' || tag === 'textarea' || tag === 'select';

    // `/` key opens palette (only when not in an input field)
    if (e.key === '/' && !isInput && !isEditable && !isOpen) {
      e.preventDefault();
      openPalette();
      return;
    }

    // `Ctrl+K` or `Cmd+K` opens/closes palette (works everywhere)
    if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
      e.preventDefault();
      if (isOpen) {
        closePalette();
      } else {
        openPalette();
      }
      return;
    }

    // `Escape` closes palette
    if (e.key === 'Escape' && isOpen) {
      e.preventDefault();
      closePalette();
      return;
    }
  });

  // Input-specific keyboard events (arrow keys, Enter)
  input.addEventListener('keydown', function (e) {
    // Arrow Down — move selection down
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (selectedIndex < filteredCommands.length - 1) {
        selectedIndex++;
        renderResults();
      }
      return;
    }

    // Arrow Up — move selection up
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (selectedIndex > 0) {
        selectedIndex--;
        renderResults();
      }
      return;
    }

    // Enter — execute selected command
    if (e.key === 'Enter') {
      e.preventDefault();
      if (filteredCommands.length > 0 && filteredCommands[selectedIndex]) {
        executeCommand(filteredCommands[selectedIndex], input.value);
      }
      return;
    }
  });

  // Input typing — filter commands as user types
  input.addEventListener('input', function () {
    filterCommands(input.value);
  });

  // Click overlay to close
  overlay.addEventListener('click', function (e) {
    if (e.target === overlay) {
      closePalette();
    }
  });

  // ── Hint Text ──
  // Update the hint text in the topbar or wherever it's displayed
  if (hintText) {
    hintText.textContent = 'Press / or Ctrl+K';
  }

})();
