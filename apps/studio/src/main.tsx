import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ConfigChecker } from './ConfigChecker.js';
import './styles.css';

const container = document.getElementById('root');
if (container === null) {
  throw new Error('The #root element is missing from index.html.');
}

createRoot(container).render(
  <StrictMode>
    <ConfigChecker />
  </StrictMode>,
);
