import React from 'react';
import rescuarLogo from '../assets/rescuar-brand-logo.png';
import './BrandLogo.css';

/** Shared brand image for all admin, login, and registration surfaces. */
export default function BrandLogo({ variant = 'header', className = '' }) {
  return (
    <img
      src={rescuarLogo}
      alt="RescuAR"
      className={`rescuar-brand-logo rescuar-brand-logo--${variant} ${className}`.trim()}
      width="1845"
      height="592"
      draggable="false"
    />
  );
}
