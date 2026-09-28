import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './index.css';
import { aplicarTema, temaPreferido } from './tema/tema';

// antes do primeiro desenho, para a tela não piscar clara em quem escolheu o escuro
aplicarTema(temaPreferido());

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
