export const CLOUDINARY_CONFIG = {
  cloudName: 'dz15gvgpl',
  uploadPreset: 'rescuAR_EvacuationCenter'
};

/**
 * Uploads an image file to Cloudinary.
 * @param {File|Blob} file - Image file to upload
 * @param {string} [preset] - Cloudinary unsigned upload preset name
 * @returns {Promise<string>} - The secure URL of the uploaded image
 */
export const uploadImageToCloudinary = async (file, preset = CLOUDINARY_CONFIG.uploadPreset) => {
  if (!file) {
    throw new Error('No file provided for upload.');
  }

  const formData = new FormData();
  formData.append('file', file);
  formData.append('upload_preset', preset);

  const url = `https://api.cloudinary.com/v1_1/${CLOUDINARY_CONFIG.cloudName}/image/upload`;

  const response = await fetch(url, {
    method: 'POST',
    body: formData
  });

  if (!response.ok) {
    const errorData = await response.json().catch(() => ({}));
    const errorMessage = errorData.error?.message || `Upload failed with status ${response.status}`;
    throw new Error(errorMessage);
  }

  const data = await response.json();
  const secureUrl = data.secure_url || data.url;

  if (!secureUrl) {
    throw new Error('Cloudinary response did not contain an image URL.');
  }

  return secureUrl;
};
