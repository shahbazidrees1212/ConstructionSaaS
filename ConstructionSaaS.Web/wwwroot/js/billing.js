(() => {
  document.querySelectorAll('[data-print-invoice]').forEach(button => button.addEventListener('click', () => window.print()));
  const lines = document.getElementById('invoice-lines');
  const add = document.getElementById('add-invoice-line');
  if (!lines || !add) return;
  function renumber() {
    const rows = Array.from(lines.children);
    rows.forEach((row, index) => {
      row.querySelectorAll('input').forEach(input => {
        input.name = input.name.replace(/Lines\[\d+\]/, `Lines[${index}]`);
        input.removeAttribute('id');
      });
      row.querySelector('button').disabled = rows.length === 1;
    });
    add.disabled = rows.length >= 20;
  }
  add.addEventListener('click', () => {
    if (lines.children.length >= 20) return;
    const row = lines.firstElementChild.cloneNode(true);
    row.querySelectorAll('input').forEach(input => input.value = input.name.endsWith('Quantity') ? '1' : '');
    lines.appendChild(row); renumber();
  });
  lines.addEventListener('click', event => {
    if (event.target.matches('.remove-line') && lines.children.length > 1) { event.target.closest('.invoice-line').remove(); renumber(); }
  });
  renumber();
})();
