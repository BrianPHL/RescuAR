import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import AuthWrapper from './AuthWrapper.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <AuthWrapper />
  </StrictMode>,
)
